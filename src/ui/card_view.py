"""
卡片视图组件
"""
import tkinter as tk
from tkinter import ttk
from typing import List, Callable, Optional, Dict, Any

from ..database.models import ClipboardItem
from ..utils.text_processor import truncate_text


class CardView(ttk.Frame):
    """卡片视图组件"""

    def __init__(self, parent, items: List[ClipboardItem] = None, 
                 on_card_click: Callable[[ClipboardItem], None] = None,
                 on_card_right_click: Callable[[ClipboardItem, tk.Event], None] = None):
        """初始化卡片视图

        Args:
            parent: 父窗口
            items: 要显示的项目列表
            on_card_click: 卡片点击回调函数
            on_card_right_click: 卡片右键点击回调函数
        """
        super().__init__(parent)
        self.parent = parent
        self.items = items or []
        self.on_card_click = on_card_click
        self.on_card_right_click = on_card_right_click
        
        self.cards = []  # 卡片控件列表
        
        # 卡片布局配置
        self.columns = 2  # 设置为2列布局
        self.card_width = 320  # 减小卡片宽度，从400调整为320
        self.card_padding = 8  # 减小内边距，从12调整为8
        self.card_margin = 8   # 减小外边距，从10调整为8
        
        self._create_widgets()
        self.update_cards(self.items)

    def _create_widgets(self):
        """创建控件"""
        # 创建滚动区域
        self.canvas = tk.Canvas(self, borderwidth=0, highlightthickness=0)
        self.scrollbar = ttk.Scrollbar(self, orient=tk.VERTICAL, command=self.canvas.yview)
        self.scrollable_frame = ttk.Frame(self.canvas)
        
        self.scrollable_frame.bind(
            "<Configure>",
            lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all"))
        )
        
        self.canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        self.canvas.configure(yscrollcommand=self.scrollbar.set)
        
        # 布局
        self.canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        
        # 绑定鼠标滚轮事件
        self.canvas.bind_all("<MouseWheel>", self._on_mousewheel)
        
        # 创建网格布局容器
        self.grid_container = ttk.Frame(self.scrollable_frame)
        self.grid_container.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)

    def _on_mousewheel(self, event):
        """鼠标滚轮事件处理"""
        self.canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")

    def update_cards(self, items: List[ClipboardItem]):
        """更新卡片列表

        Args:
            items: 要显示的项目列表
        """
        self.items = items
        
        # 清除现有卡片
        for card in self.cards:
            card.destroy()
        self.cards = []
        
        # 清除网格容器中的所有部件
        for widget in self.grid_container.winfo_children():
            widget.destroy()
            
        # 如果没有卡片，显示提示信息
        if not self.items:
            empty_label = ttk.Label(self.grid_container, text="没有内容", font=("", 12))
            empty_label.grid(row=0, column=0, pady=20, padx=20)
            self.cards.append(empty_label)
            return
            
        # 创建新卡片并使用网格布局
        for i, item in enumerate(self.items):
            row = i // self.columns
            col = i % self.columns
            
            card = self._create_card(item)
            card.grid(row=row, column=col, padx=self.card_margin, 
                     pady=self.card_margin, sticky="nsew")
            self.cards.append(card)
            
        # 配置网格列的权重，使其均匀分布
        for i in range(self.columns):
            self.grid_container.columnconfigure(i, weight=1)

    def _create_card(self, item: ClipboardItem) -> ttk.Frame:
        """创建单个卡片

        Args:
            item: 剪贴板项目

        Returns:
            卡片控件
        """
        # 创建卡片主框架 - 增强边框可见性
        card = ttk.Frame(self.grid_container, style="Card.TFrame")
        card.item = item  # 将项目对象附加到卡片控件
        
        # 设置卡片最小宽度和最小高度
        card.configure(width=self.card_width)
        
        # 使用网格布局管理器替代pack，以更好地控制布局
        inner_frame = ttk.Frame(card, padding=self.card_padding)
        inner_frame.pack(fill=tk.BOTH, expand=True, padx=1, pady=1)
        
        # 配置行列权重，使内容区域可以扩展
        inner_frame.columnconfigure(0, weight=1)
        inner_frame.rowconfigure(2, weight=1)  # 内容区域可以扩展
        
        # 标题和类型行（第0行）
        header_frame = ttk.Frame(inner_frame)
        header_frame.grid(row=0, column=0, sticky="ew", pady=(0, 6))
        
        # 标题
        title_font_size = 12
        title_label = ttk.Label(header_frame, text=item.title, font=("", title_font_size, "bold"))
        title_label.pack(side=tk.LEFT)
        
        # 类型标签
        type_frame = ttk.Frame(header_frame, style="TypeBadge.TFrame")
        type_frame.pack(side=tk.RIGHT, padx=1, pady=1)
        
        type_label = ttk.Label(type_frame, text=item.item_type, 
                              font=("", 9), padding=(4, 1))
        type_label.pack()
        
        # 分隔线（第1行）
        separator = ttk.Separator(inner_frame, orient="horizontal")
        separator.grid(row=1, column=0, sticky="ew", pady=4)
        
        # 内容预览（第2行）- 使用Frame包装以实现可扩展
        content_container = ttk.Frame(inner_frame)
        content_container.grid(row=2, column=0, sticky="nsew", pady=0)
        
        # 确保content_container可以扩展
        content_container.columnconfigure(0, weight=1)
        content_container.rowconfigure(0, weight=1)
        
        wrap_width = self.card_width - (self.card_padding * 2) - 10
        
        # 根据卡片大小调整预览字符数
        content_preview = truncate_text(item.content, 120)  # 减少为120字符，更适合显示区域
        
        content_label = ttk.Label(content_container, text=content_preview, 
                                 wraplength=wrap_width,
                                 justify=tk.LEFT,
                                 anchor="nw",  # 设置文本锚点为左上角(northwest)
                                 font=("", 9))
        content_label.pack(fill=tk.BOTH, expand=True, pady=6, anchor="nw")
        
        # 内容容器高度自适应，但保持最小高度
        # 移除固定高度限制，但使用最小高度
        content_container.pack_propagate(True)  # 允许根据内容调整大小
        
        # 底部信息（第3行）- 始终在底部
        footer_frame = ttk.Frame(inner_frame)
        footer_frame.grid(row=3, column=0, sticky="ew", pady=(6, 0))
        
        usage_label = ttk.Label(footer_frame, text=f"使用次数: {item.usage_count}", 
                              font=("", 8))
        usage_label.pack(side=tk.LEFT)
        
        # 简化最后使用时间的显示格式
        if item.usage_count == 0:
            last_used_text = "从未使用"
        else:
            last_used_text = f"最后: {item.last_used.strftime('%Y-%m-%d %H:%M')}"
            
        last_used_label = ttk.Label(footer_frame, text=last_used_text, 
                                  font=("", 8))
        last_used_label.pack(side=tk.RIGHT)
        
        # 设置最小高度以确保卡片有足够空间
        min_height = 160  # 增加最小高度，确保有足够空间显示内容
        card.configure(height=min_height)
        
        # 绑定事件
        for widget in [card, inner_frame, header_frame, title_label, content_label, footer_frame]:
            widget.bind("<Double-1>", lambda e, i=item: self._on_card_click(i))
            widget.bind("<Button-3>", lambda e, i=item: self._on_card_right_click(i, e))
        
        return card

    def _on_card_click(self, item: ClipboardItem):
        """卡片点击事件处理

        Args:
            item: 被点击的项目
        """
        if self.on_card_click:
            self.on_card_click(item)

    def _on_card_right_click(self, item: ClipboardItem, event: tk.Event):
        """卡片右键点击事件处理

        Args:
            item: 被点击的项目
            event: 事件对象
        """
        if self.on_card_right_click:
            self.on_card_right_click(item, event) 