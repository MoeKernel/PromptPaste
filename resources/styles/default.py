"""
默认样式配置
"""
import tkinter as tk
from tkinter import ttk
from tkinter.font import Font


def apply_styles(style: ttk.Style):
    """应用默认样式

    Args:
        style: ttk.Style对象
    """
    # 创建基础样式
    style.configure("TFrame", background="#ffffff")
    style.configure("TLabel", background="#ffffff", foreground="#333333")
    style.configure("TButton", padding=6, relief="flat", background="#f0f0f0")
    style.configure("TEntry", padding=6)
    
    # 卡片样式 - 使用可靠的边框设置
    style.configure(
        "Card.TFrame", 
        background="#ffffff",
        relief="ridge",  # 使用可靠的ridge边框样式
        borderwidth=1,   # 确保边框可见
        padding=2        # 内边距
    )
    
    # 类型徽章样式 - 简化设计确保可见
    style.configure(
        "TypeBadge.TFrame",
        background="#e8f0f8", 
        relief="flat",
        borderwidth=1,
        padding=3
    )
    
    # 设置字体
    default_font = Font(family="Segoe UI", size=10)
    style.configure("TLabel", font=default_font)
    style.configure("TButton", font=default_font)
    
    # 按钮特殊样式
    style.configure(
        "TButton", 
        background="#f0f0f0", 
        foreground="#333333",
        borderwidth=1, 
        focusthickness=0,
        padding=(10, 5)
    )
    
    # 按钮悬停效果
    style.map(
        "TButton",
        background=[("active", "#e0e0e0"), ("pressed", "#d0d0d0")],
        relief=[("pressed", "flat")]
    )
    
    # 移除按钮焦点边框
    style.configure("TButton", focuscolor=style.configure(".")["background"])
    
    # 标签样式
    style.configure(
        "TLabel",
        font=("", 10)
    )
    
    # 标题标签样式
    style.configure(
        "Title.TLabel",
        font=("", 12, "bold")
    )
    
    # 输入框样式
    style.configure(
        "TEntry",
        padding=5
    )
    
    # 自定义Switch.TCheckbutton样式，让它更像开关按钮
    style.configure(
        "Switch.TCheckbutton",
        background="#ffffff",
        foreground="#333333",
        font=("", 9),
        relief="flat",
        padding=2,
        focuscolor=style.configure(".")["background"]  # 设置焦点颜色与背景相同，移除虚线框
    )
    
    # 切换状态时的颜色变化
    style.map(
        "Switch.TCheckbutton",
        background=[("active", "#f0f0f0"), ("selected", "#e6f7ff")],
        foreground=[("selected", "#1890ff")]
    ) 