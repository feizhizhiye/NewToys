# -*- coding: utf-8 -*-
# NewToys 示例脚本 · Python
# 用法：脚本库 →「示例：环境速览（Python）」→ ▶ 运行
# 前提：本机装了 Python，且 .py 有默认打开方式（否则 Windows 会弹「如何打开」）。

import datetime
import os
import platform
import sys

print("==== NewToys 示例脚本（Python）====")
print(f"时间       ：{datetime.datetime.now():%Y-%m-%d %H:%M:%S}")
print(f"解释器     ：Python {platform.python_version()}  ({sys.executable})")
print(f"计算机名   ：{platform.node()}")
print(f"当前用户   ：{os.environ.get('USERNAME', '')}")
print(f"工作目录   ：{os.getcwd()}")
print()
print("提示：examples 目录里的脚本可以随便删改；想加自己的脚本，直接放进脚本目录再「🔍 扫描目录」。")
