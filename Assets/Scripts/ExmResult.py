import torch
import numpy as np
import lpips
from skimage.metrics import structural_similarity as ssim
from skimage.metrics import peak_signal_noise_ratio as psnr
import cv2
import os
import sys
import csv

def eval_metrics(img1, img2):
    """
    img1, img2: numpy arrays or torch tensors, shape [H, W, C], range [0, 1] or [0, 255]
    Returns: dict with SSIM, PSNR, LPIPS
    """
    # Convert to numpy if needed
    if isinstance(img1, torch.Tensor):
        img1 = img1.detach().cpu().numpy()
    if isinstance(img2, torch.Tensor):
        img2 = img2.detach().cpu().numpy()

    # Ensure float32 and range [0, 1]
    img1 = img1.astype(np.float32)
    img2 = img2.astype(np.float32)
    if img1.max() > 1.1: img1 /= 255.
    if img2.max() > 1.1: img2 /= 255.
    
    min_side = min(img1.shape[0], img1.shape[1])
    win_size = min(7, min_side if min_side % 2 == 1 else min_side - 1)
    if win_size < 3: win_size = 3  # 最小窗口为3

    # SSIM & PSNR (skimage expects [H, W, C])
    ssim_val = ssim(img1, img2, data_range=1.0, win_size=win_size, channel_axis=-1)
    psnr_val = psnr(img1, img2, data_range=1.0)

    # LPIPS (expects torch tensor [B,3,H,W], range [-1,1])
    loss_fn = lpips.LPIPS(net='alex')
    img1_lpips = torch.from_numpy(img1).permute(2,0,1).unsqueeze(0) * 2 - 1
    img2_lpips = torch.from_numpy(img2).permute(2,0,1).unsqueeze(0) * 2 - 1
    lpips_val = loss_fn(img1_lpips, img2_lpips).item()

    return {'SSIM': ssim_val, 'PSNR': psnr_val, 'LPIPS': lpips_val}

def to_binary_gray(img):
    # 转为灰度
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    # 二值化
    _, binary = cv2.threshold(gray, 127, 255, cv2.THRESH_BINARY)
    # 转回三通道
    binary_rgb = cv2.cvtColor(binary, cv2.COLOR_GRAY2RGB)
    return binary_rgb

def process_folder(folder_path):
    results = []
    for fname in os.listdir(folder_path):
        if fname.startswith("original_") and fname.endswith(".png"):
            # 获取x
            x = fname.split("_")[1].split(".")[0]
            result_fname = f"result_{x}.png"
            original_path = os.path.join(folder_path, fname)
            result_path = os.path.join(folder_path, result_fname)
            if not os.path.exists(result_path):
                print(f"Result file not found for {x}")
                continue

            # 读取图片
            original_img = cv2.imread(original_path)
            result_img = cv2.imread(result_path)

            # 转为二值灰度图
            original_bin = to_binary_gray(original_img)
            result_bin = to_binary_gray(result_img)
             # 保存二值灰度图
            cv2.imwrite(os.path.join(folder_path, f"binary_original_{x}.png"), original_bin)
            cv2.imwrite(os.path.join(folder_path, f"binary_result_{x}.png"), result_bin)

            # 转为float32并归一化
            original_bin = original_bin.astype(np.float32) / 255.
            result_bin = result_bin.astype(np.float32) / 255.

            # 调用评价指标
            metrics = eval_metrics(original_bin, result_bin)
            print(f"x={x}: {metrics}")
            results.append((x, metrics))
    return results

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("用法: python ExmResult.py <Resources下的目标文件夹名>")
        sys.exit(1)
    folder_name = sys.argv[1]
    folder_path = os.path.join("Assets", "Resources", folder_name)
    results = process_folder(folder_path)
    # 保存为CSV
    csv_path = os.path.join(folder_path, "metrics_results.csv")
    with open(csv_path, "w", newline='', encoding='utf-8') as f:
        writer = csv.writer(f)
        writer.writerow(["x", "SSIM", "PSNR", "LPIPS"])
        for x, metrics in results:
            writer.writerow([x, metrics["SSIM"], metrics["PSNR"], metrics["LPIPS"]])
    print(f"结果已保存到 {csv_path}")
