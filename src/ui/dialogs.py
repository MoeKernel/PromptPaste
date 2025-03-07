"""
对话框组件
"""
import tkinter as tk
from tkinter import ttk, simpledialog, messagebox
from typing import Optional, List, Callable, Dict

from ..database.models import ClipboardItem
from ..utils.text_processor import extract_variables


class AddEditDialog(tk.Toplevel):
    """添加/编辑剪贴板项目对话框"""

    def __init__(self, parent, item: Optional[ClipboardItem] = None, item_types: List[str] = None,
                 on_save: Callable[[ClipboardItem], None] = None):
        """初始化对话框

        Args:
            parent: 父窗口
            item: 要编辑的项目，如果为None则为添加模式
            item_types: 可选的项目类型列表
            on_save: 保存回调函数
        """
        super().__init__(parent)
        self.parent = parent
        self.item = item or ClipboardItem()
        self.item_types = item_types or ["AI提示词", "商务话术", "其他"]
        self.on_save = on_save

        self.title("编辑内容" if item else "添加内容")
        self.geometry("500x400")
        self.resizable(True, True)
        self.transient(parent)
        self.grab_set()
        
        self._create_widgets()
        self._load_item_data()
        
        # 居中显示
        self.update_idletasks()
        width = self.winfo_width()
        height = self.winfo_height()
        x = (self.winfo_screenwidth() // 2) - (width // 2)
        y = (self.winfo_screenheight() // 2) - (height // 2)
        self.geometry(f"{width}x{height}+{x}+{y}")

    def _create_widgets(self):
        """创建对话框控件"""
        # 主框架
        main_frame = ttk.Frame(self, padding=10)
        main_frame.pack(fill=tk.BOTH, expand=True)

        # 标题
        ttk.Label(main_frame, text="标题:").grid(row=0, column=0, sticky=tk.W, pady=(0, 5))
        self.title_entry = ttk.Entry(main_frame, width=50)
        self.title_entry.grid(row=0, column=1, sticky=tk.EW, pady=(0, 5))

        # 类型
        ttk.Label(main_frame, text="类型:").grid(row=1, column=0, sticky=tk.W, pady=(0, 5))
        self.type_combo = ttk.Combobox(main_frame, values=self.item_types)
        self.type_combo.grid(row=1, column=1, sticky=tk.EW, pady=(0, 5))
        
        # 如果是新类型，可以直接输入
        self.type_combo.configure(state="normal")

        # 内容
        ttk.Label(main_frame, text="内容:").grid(row=2, column=0, sticky=tk.NW, pady=(0, 5))
        
        # 文本框和滚动条
        text_frame = ttk.Frame(main_frame)
        text_frame.grid(row=2, column=1, sticky=tk.NSEW, pady=(0, 5))
        
        self.content_text = tk.Text(text_frame, wrap=tk.WORD, width=50, height=10)
        scrollbar = ttk.Scrollbar(text_frame, orient=tk.VERTICAL, command=self.content_text.yview)
        self.content_text.configure(yscrollcommand=scrollbar.set)
        
        self.content_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        
        # 变量提示
        ttk.Label(main_frame, text="提示: 使用 {{变量名}} 格式添加可替换的变量").grid(
            row=3, column=0, columnspan=2, sticky=tk.W, pady=(5, 10))

        # 按钮框架
        button_frame = ttk.Frame(main_frame)
        button_frame.grid(row=4, column=0, columnspan=2, sticky=tk.EW)
        
        ttk.Button(button_frame, text="保存", command=self._on_save).pack(side=tk.RIGHT, padx=5)
        ttk.Button(button_frame, text="取消", command=self.destroy).pack(side=tk.RIGHT, padx=5)
        
        # 设置网格权重
        main_frame.columnconfigure(1, weight=1)
        main_frame.rowconfigure(2, weight=1)

    def _load_item_data(self):
        """加载项目数据到控件"""
        if self.item.id is not None:  # 编辑模式
            self.title_entry.insert(0, self.item.title)
            self.type_combo.set(self.item.item_type)
            self.content_text.insert("1.0", self.item.content)

    def _on_save(self):
        """保存按钮点击事件"""
        title = self.title_entry.get().strip()
        item_type = self.type_combo.get().strip()
        content = self.content_text.get("1.0", tk.END).strip()
        
        if not title:
            messagebox.showerror("错误", "标题不能为空")
            return
        
        if not content:
            messagebox.showerror("错误", "内容不能为空")
            return
        
        if not item_type:
            item_type = "AI提示词"  # 默认类型
        
        # 更新项目数据
        self.item.title = title
        self.item.content = content
        self.item.item_type = item_type
        
        # 调用保存回调
        if self.on_save:
            self.on_save(self.item)
        
        self.destroy()


class ClipboardSaveDialog(tk.Toplevel):
    """剪贴板内容保存对话框"""

    def __init__(self, parent, content: str, item_types: List[str] = None,
                 on_save: Callable[[ClipboardItem], None] = None):
        """初始化对话框

        Args:
            parent: 父窗口
            content: 剪贴板内容
            item_types: 可选的项目类型列表
            on_save: 保存回调函数
        """
        super().__init__(parent)
        self.parent = parent
        self.content = content
        self.item_types = item_types or ["AI提示词", "商务话术", "其他"]
        self.on_save = on_save

        self.title("保存剪贴板内容")
        self.geometry("500x400")
        self.resizable(True, True)
        self.transient(parent)
        self.grab_set()
        
        self._create_widgets()
        
        # 居中显示
        self.update_idletasks()
        width = self.winfo_width()
        height = self.winfo_height()
        x = (self.winfo_screenwidth() // 2) - (width // 2)
        y = (self.winfo_screenheight() // 2) - (height // 2)
        self.geometry(f"{width}x{height}+{x}+{y}")
        
        # 自动生成标题
        self._generate_title()

    def _create_widgets(self):
        """创建对话框控件"""
        # 主框架
        main_frame = ttk.Frame(self, padding=10)
        main_frame.pack(fill=tk.BOTH, expand=True)

        # 标题
        ttk.Label(main_frame, text="标题:").grid(row=0, column=0, sticky=tk.W, pady=(0, 5))
        self.title_entry = ttk.Entry(main_frame, width=50)
        self.title_entry.grid(row=0, column=1, sticky=tk.EW, pady=(0, 5))

        # 类型
        ttk.Label(main_frame, text="类型:").grid(row=1, column=0, sticky=tk.W, pady=(0, 5))
        self.type_combo = ttk.Combobox(main_frame, values=self.item_types)
        self.type_combo.grid(row=1, column=1, sticky=tk.EW, pady=(0, 5))
        
        # 如果是新类型，可以直接输入
        self.type_combo.configure(state="normal")

        # 内容
        ttk.Label(main_frame, text="内容:").grid(row=2, column=0, sticky=tk.NW, pady=(0, 5))
        
        # 文本框和滚动条
        text_frame = ttk.Frame(main_frame)
        text_frame.grid(row=2, column=1, sticky=tk.NSEW, pady=(0, 5))
        
        self.content_text = tk.Text(text_frame, wrap=tk.WORD, width=50, height=10)
        scrollbar = ttk.Scrollbar(text_frame, orient=tk.VERTICAL, command=self.content_text.yview)
        self.content_text.configure(yscrollcommand=scrollbar.set)
        
        self.content_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        
        # 插入剪贴板内容
        self.content_text.insert("1.0", self.content)
        
        # 变量提示
        ttk.Label(main_frame, text="提示: 使用 {{变量名}} 格式添加可替换的变量").grid(
            row=3, column=0, columnspan=2, sticky=tk.W, pady=(5, 10))

        # 按钮框架
        button_frame = ttk.Frame(main_frame)
        button_frame.grid(row=4, column=0, columnspan=2, sticky=tk.EW)
        
        ttk.Button(button_frame, text="保存", command=self._on_save).pack(side=tk.RIGHT, padx=5)
        ttk.Button(button_frame, text="取消", command=self.destroy).pack(side=tk.RIGHT, padx=5)
        
        # 设置网格权重
        main_frame.columnconfigure(1, weight=1)
        main_frame.rowconfigure(2, weight=1)

    def _generate_title(self):
        """根据内容自动生成标题"""
        # 使用内容的前20个字符作为标题
        title = self.content.strip()
        if len(title) > 20:
            title = title[:20] + "..."
        self.title_entry.insert(0, title)

    def _on_save(self):
        """保存按钮点击事件"""
        title = self.title_entry.get().strip()
        item_type = self.type_combo.get().strip()
        content = self.content_text.get("1.0", tk.END).strip()
        
        if not title:
            messagebox.showerror("错误", "标题不能为空")
            return
        
        if not content:
            messagebox.showerror("错误", "内容不能为空")
            return
        
        if not item_type:
            item_type = "AI提示词"  # 默认类型
        
        # 创建新项目
        item = ClipboardItem(
            title=title,
            content=content,
            item_type=item_type
        )
        
        # 调用保存回调
        if self.on_save:
            self.on_save(item)
        
        self.destroy()


class VariableInputDialog(tk.Toplevel):
    """变量输入对话框"""

    def __init__(self, parent, variables: List[str], on_submit: Callable[[Dict[str, str]], None]):
        """初始化对话框

        Args:
            parent: 父窗口
            variables: 变量名列表
            on_submit: 提交回调函数
        """
        super().__init__(parent)
        self.parent = parent
        self.variables = variables
        self.on_submit = on_submit
        self.variable_entries = {}

        self.title("输入变量值")
        self.geometry("400x300")
        self.resizable(True, True)
        self.transient(parent)
        self.grab_set()
        
        self._create_widgets()
        
        # 居中显示
        self.update_idletasks()
        width = self.winfo_width()
        height = self.winfo_height()
        x = (self.winfo_screenwidth() // 2) - (width // 2)
        y = (self.winfo_screenheight() // 2) - (height // 2)
        self.geometry(f"{width}x{height}+{x}+{y}")

    def _create_widgets(self):
        """创建对话框控件"""
        # 主框架
        main_frame = ttk.Frame(self, padding=10)
        main_frame.pack(fill=tk.BOTH, expand=True)
        
        # 说明标签
        ttk.Label(main_frame, text="请为以下变量输入值:").grid(
            row=0, column=0, columnspan=2, sticky=tk.W, pady=(0, 10))
        
        # 变量输入框
        for i, var_name in enumerate(self.variables):
            ttk.Label(main_frame, text=f"{var_name}:").grid(
                row=i+1, column=0, sticky=tk.W, pady=(0, 5))
            
            entry = ttk.Entry(main_frame, width=30)
            entry.grid(row=i+1, column=1, sticky=tk.EW, pady=(0, 5))
            
            self.variable_entries[var_name] = entry
        
        # 按钮框架
        button_frame = ttk.Frame(main_frame)
        button_frame.grid(row=len(self.variables)+1, column=0, columnspan=2, sticky=tk.EW, pady=(10, 0))
        
        ttk.Button(button_frame, text="确定", command=self._on_submit).pack(side=tk.RIGHT, padx=5)
        ttk.Button(button_frame, text="取消", command=self.destroy).pack(side=tk.RIGHT, padx=5)
        
        # 设置网格权重
        main_frame.columnconfigure(1, weight=1)

    def _on_submit(self):
        """确定按钮点击事件"""
        # 收集变量值
        variable_values = {}
        for var_name, entry in self.variable_entries.items():
            variable_values[var_name] = entry.get()
        
        # 调用提交回调
        if self.on_submit:
            self.on_submit(variable_values)
        
        self.destroy() 