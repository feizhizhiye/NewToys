# -*- coding: utf-8 -*-
"""
NewToys 示例脚本（.py → 由 python 运行，命令里带了 -X utf8，中文输出不会乱码）

用法：
    python list-large-files.py [目录] [数量]
不传参数时：扫描本脚本所在目录，列出最大的 20 个文件。

只读脚本：不删、不改任何文件。
"""

import os
import sys
import time


def human(n):
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if n < 1024 or unit == "TB":
            return f"{n:,.2f} {unit}" if unit != "B" else f"{n:,.0f} B"
        n /= 1024.0


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    folder = sys.argv[1] if len(sys.argv) > 1 else here
    top = int(sys.argv[2]) if len(sys.argv) > 2 else 20

    if not os.path.isdir(folder):
        print(f"[失败] 目录不存在：{folder}")
        return 1

    print(f"扫描目录：{folder}")
    files = []
    for root, _dirs, names in os.walk(folder):
        for name in names:
            path = os.path.join(root, name)
            try:
                files.append((os.path.getsize(path), path))
            except OSError:
                pass          # 无权限 / 扫描中被删掉的条目：跳过就好

    if not files:
        print("这个目录里没有文件。")
        return 0

    files.sort(reverse=True)
    print(f"共 {len(files)} 个文件，最大的 {min(top, len(files))} 个：")
    for size, path in files[:top]:
        stamp = time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(path)))
        print(f"  {human(size):>12}  {stamp}  {os.path.relpath(path, folder)}")

    print("[完成]")
    return 0


if __name__ == "__main__":
    sys.exit(main())
