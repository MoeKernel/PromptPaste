#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
PromptPaste - 剪贴板管理工具
主程序入口
"""
import os
import sys
import tkinter as tk
from pathlib import Path

from src.ui.main_window import MainWindow
from src.utils.init_app import ensure_app_dirs, create_sample_data, backup_database
from src.database.db_manager import DatabaseManager


def main():
    """程序主入口"""
    # 确保当前工作目录是程序所在目录
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    
    # 初始化应用程序目录
    data_dir = ensure_app_dirs()
    
    # 设置数据库路径
    db_path = os.path.join(data_dir, "promptpaste.db")
    
    # 创建数据库管理器
    db_manager = DatabaseManager(db_path)
    
    # 创建示例数据
    samples = create_sample_data(db_path)
    if samples:
        for sample in samples:
            db_manager.add_item(sample)
    
    # 创建并运行主窗口
    app = MainWindow(db_manager)
    app.run()
    
    # 备份数据库
    backup_dir = os.path.join(data_dir, "backups")
    backup_database(db_path, backup_dir)


if __name__ == "__main__":
    main() 