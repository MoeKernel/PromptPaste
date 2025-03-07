#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
PromptPaste打包脚本
用于将项目打包为可执行文件(exe)
"""
import os
import subprocess
import sys


def build_exe():
    """
    使用PyInstaller打包为exe文件
    """
    print("开始打包PromptPaste为exe文件...")
    
    # 构建PyInstaller命令
    cmd = [
        "python", "-m", "PyInstaller",
        "--name=PromptPaste",  # 指定输出文件名
        "--onefile",           # 打包为单个exe文件
        "--noconsole",         # 运行时不显示控制台
        "--icon=resources/icons/app_icon.ico",  # 使用应用图标
        "--add-data=resources;resources",  # 添加资源文件
        "main.py"              # 主程序入口
    ]
    
    # 执行PyInstaller命令
    try:
        result = subprocess.run(cmd, check=True)
        if result.returncode == 0:
            print("\n✅ 打包成功！")
            print(f"\n可执行文件位置: {os.path.abspath('dist/PromptPaste.exe')}")
            print("\n使用说明:")
            print("1. 可执行文件在dist目录下")
            print("2. 首次运行会在用户主目录下创建.promptpaste文件夹存储数据")
            print("3. 如需迁移数据，请备份.promptpaste文件夹中的promptpaste.db文件")
        else:
            print("\n❌ 打包失败，请检查错误信息。")
    except Exception as e:
        print(f"\n❌ 打包过程出现错误: {e}")


if __name__ == "__main__":
    build_exe() 