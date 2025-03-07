"""
主窗口模块
"""
import json
import os
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
from typing import List, Dict, Optional
from PIL import Image, ImageTk
import sys
import datetime  # 添加datetime模块

from ttkthemes import ThemedTk

from ..database.db_manager import DatabaseManager
from ..database.models import ClipboardItem
from ..utils.clipboard import ClipboardMonitor, copy_to_clipboard
from ..utils.text_processor import extract_variables, replace_variables
from .card_view import CardView
from .dialogs import AddEditDialog, ClipboardSaveDialog, VariableInputDialog

# 导入样式
try:
    from resources.styles.default import apply_styles
except ImportError:
    # 如果样式模块不可用，提供一个空的样式函数
    def apply_styles(style):
        pass


class MainWindow:
    """主窗口类"""

    def __init__(self, db_manager: DatabaseManager):
        """初始化主窗口

        Args:
            db_manager: 数据库管理器实例
        """
        # 使用传入的数据库管理器
        self.db_manager = db_manager
        
        # 项目类型列表（用于在不同线程间共享）
        self.item_types = []
        
        # 创建主窗口
        self.root = ThemedTk(theme="arc")  # 使用arc主题
        self.root.title("PromptPaste")
        self.root.geometry("915x580")  # 从1000x600调整为920x580，更符合卡片区域所需的空间
        self.root.minsize(780, 480)    # 从800x500调整为780x480，保持紧凑型界面
        
        # 添加应用前台状态标记
        self.is_foreground = False
        
        # 添加剪贴板监听状态标记
        self.clipboard_monitor_enabled = True  # 默认开启剪贴板监听
        
        # 设置窗口管理器事件处理
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)
        self.root.bind("<Map>", self._on_map)
        self.root.bind("<Unmap>", self._on_unmap)
        
        # 添加焦点事件绑定
        self.root.bind("<FocusIn>", self._on_focus_in)
        self.root.bind("<FocusOut>", self._on_focus_out)
        
        # 设置窗口图标和任务栏图标
        try:
            # 设置应用程序 ID (AppUserModelID)
            if sys.platform.startswith('win'):
                try:
                    import ctypes
                    myappid = "PromptPaste.Application.1.0"  # 任意字符串，但应该是唯一的
                    ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)
                except Exception as e:
                    print(f"设置应用程序 ID 失败: {e}")
            
            # 尝试加载图标 - 使用 PhotoImage 而不是 iconbitmap
            icon_path = 'resources/icons/app_icon.png'  # 使用 PNG 格式而不是 ICO
            if os.path.exists(icon_path):
                icon_image = tk.PhotoImage(file=icon_path)
                self.root.iconphoto(True, icon_image)
                self.root.tk.call('wm', 'iconphoto', self.root._w, icon_image)
            else:
                print(f"图标文件不存在: {icon_path}")
        except Exception as e:
            print(f"无法加载应用图标: {e}")
        
        # 设置样式
        self.style = ttk.Style()
        self.style.configure("Card.TFrame", relief=tk.RAISED, borderwidth=1)
        
        # 应用自定义样式
        apply_styles(self.style)
        
        # 创建剪贴板监听器
        self.clipboard_monitor = ClipboardMonitor(self._on_clipboard_change)
        
        # 当前筛选类型
        self.current_filter_type = None
        
        # 当前搜索关键词
        self.current_search_keyword = ""
        
        
        # 创建UI组件
        self._create_widgets()
        
        # 加载数据
        self._load_data()
        
        # 启动剪贴板监听
        self.clipboard_monitor.start()

    def _create_widgets(self):
        """创建UI组件"""
        # 创建主框架
        main_frame = ttk.Frame(self.root, padding=10)
        main_frame.pack(fill=tk.BOTH, expand=True)
        
        # 创建菜单栏
        self._create_menu()
        
        # 创建顶部工具栏
        toolbar_frame = ttk.Frame(main_frame)
        toolbar_frame.pack(fill=tk.X, pady=(0, 10))
        
        # 加载图标
        add_icon_image = Image.open('resources/icons/add_icon.png')
        add_icon = ImageTk.PhotoImage(add_icon_image)
        
        # 添加按钮
        add_button = ttk.Button(toolbar_frame, image=add_icon, command=self._on_add_click)
        add_button.image = add_icon  # 防止图标被垃圾回收
        add_button.pack(side=tk.LEFT)
        
        # 移除按钮点击后的虚线边框
        add_button.bind("<FocusIn>", lambda event: add_button.master.focus_set())
        
        
        # 添加剪贴板监听切换按钮
        self.monitor_var = tk.BooleanVar(value=self.clipboard_monitor_enabled)
        self.monitor_var.trace_add("write", self._on_monitor_toggle)
        
        # 添加开关图标
        self.monitor_switch = ttk.Checkbutton(
            toolbar_frame, 
            variable=self.monitor_var,
            text="自动弹窗",
            style="Switch.TCheckbutton"
        )
        self.monitor_switch.pack(side=tk.LEFT, padx=(10, 10))
        
        # 移除切换按钮的焦点虚线框
        self.monitor_switch.bind("<FocusIn>", lambda event: toolbar_frame.focus_set())
        
        # 搜索框
        ttk.Label(toolbar_frame, text="搜索:").pack(side=tk.LEFT, padx=(0, 5))
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", self._on_search_changed)
        search_entry = ttk.Entry(toolbar_frame, textvariable=self.search_var, width=30)
        search_entry.pack(side=tk.LEFT, padx=(0, 10))
        
        # 变量输入框
        ttk.Label(toolbar_frame, text="替换:").pack(side=tk.LEFT, padx=(10, 5))
        self.variable_var = tk.StringVar()
        variable_entry = ttk.Entry(toolbar_frame, textvariable=self.variable_var, width=30)
        variable_entry.pack(side=tk.LEFT)
        
        # 添加清空按钮
        clear_button = ttk.Button(toolbar_frame, text="清空", command=self._on_clear_click)
        clear_button.pack(side=tk.LEFT, padx=(30, 0))

        # 移除清空按钮的焦点虚线框
        clear_button.bind("<FocusIn>", lambda event: toolbar_frame.focus_set())

        # 创建内容区域
        content_frame = ttk.Frame(main_frame)
        content_frame.pack(fill=tk.BOTH, expand=True)
        
        # 创建侧边栏
        sidebar_frame = ttk.Frame(content_frame, width=140)  # 从150减少到140，减少侧边栏宽度
        sidebar_frame.pack(side=tk.LEFT, fill=tk.Y, padx=(0, 10))
        sidebar_frame.pack_propagate(False)  # 防止宽度被内容撑开
        
        # 侧边栏标题
        ttk.Label(sidebar_frame, text="分类", font=("", 12, "bold")).pack(anchor=tk.W, pady=(0, 10))
        
        # 侧边栏列表
        self.type_listbox = tk.Listbox(
            sidebar_frame, 
            height=15,
            highlightthickness=0,  # 移除高亮边框
            activestyle="none",    # 移除选中项的下划线
            selectbackground="#4a6984",  # 设置选中项的背景色
            selectforeground="white"     # 设置选中项的文字颜色
        )
        self.type_listbox.pack(fill=tk.BOTH, expand=True)
        self.type_listbox.bind("<<ListboxSelect>>", self._on_type_selected)
        
        # 移除列表选中后的焦点
        self.type_listbox.bind("<FocusIn>", lambda event: self.root.focus_set())
        
        # 添加"全部"选项
        self.type_listbox.insert(tk.END, "全部")
        
        # 添加滚动条
        scrollbar = ttk.Scrollbar(sidebar_frame, orient="vertical", command=self.type_listbox.yview)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        self.type_listbox.config(yscrollcommand=scrollbar.set)
        
        # 创建卡片视图
        self.card_view = CardView(
            content_frame, 
            on_card_click=self._on_card_click,
            on_card_right_click=self._on_card_right_click
        )
        self.card_view.pack(fill=tk.BOTH, expand=True)

    def _create_menu(self):
        """创建菜单栏"""
        menubar = tk.Menu(self.root)
        
        # 文件菜单
        file_menu = tk.Menu(menubar, tearoff=0)
        file_menu.add_command(label="导入", command=self._on_import)
        file_menu.add_command(label="导出", command=self._on_export)
        file_menu.add_separator()
        file_menu.add_command(label="退出", command=self.root.quit)
        menubar.add_cascade(label="文件", menu=file_menu)
        
        # 帮助菜单
        help_menu = tk.Menu(menubar, tearoff=0)
        help_menu.add_command(label="关于", command=self._show_about)
        menubar.add_cascade(label="帮助", menu=help_menu)
        
        self.root.config(menu=menubar)

    def _load_data(self):
        """加载数据"""
        # 加载所有项目
        items = self.db_manager.get_all_items()
        self.card_view.update_cards(items)
        
        # 更新项目类型列表
        db_types = self.db_manager.get_item_types()
        if db_types:
            # 合并默认类型和数据库中的类型
            all_types = set(self.item_types + db_types)
            self.item_types = sorted(list(all_types))
        
        # 加载类型列表到UI
        self._update_type_list()

    def _update_type_list(self):
        """更新类型列表"""
        # 清除现有类型
        self.type_listbox.delete(1, tk.END)  # 保留"全部"选项
        
        # 添加到列表框
        for item_type in self.item_types:
            self.type_listbox.insert(tk.END, item_type)

    def _filter_items(self):
        """根据当前筛选条件过滤项目"""
        if self.current_filter_type and self.current_filter_type != "全部":
            # 按类型筛选
            items = self.db_manager.get_items_by_type(self.current_filter_type)
        else:
            # 获取所有项目
            items = self.db_manager.get_all_items()
        
        # 如果有搜索关键词，进一步筛选
        if self.current_search_keyword:
            items = self.db_manager.search_items(self.current_search_keyword)
            
            # 如果同时有类型筛选，需要再次过滤
            if self.current_filter_type and self.current_filter_type != "全部":
                items = [item for item in items if item.item_type == self.current_filter_type]
        
        # 更新卡片视图
        self.card_view.update_cards(items)

    def _on_monitor_toggle(self, *args):
        """剪贴板监听开关切换回调"""
        self.clipboard_monitor_enabled = self.monitor_var.get()
        # status = "启用" if self.clipboard_monitor_enabled else "禁用"
        # print(f"剪贴板监听自动弹窗已{status}")
        
    def _on_clipboard_change(self, content: str, db_manager: DatabaseManager):
        """剪贴板内容变化回调

        Args:
            content: 新的剪贴板内容
            db_manager: 数据库管理器实例
        """
        # 仅当应用处于前台且监听功能开启时弹出保存对话框
        if self.is_foreground and self.clipboard_monitor_enabled:
            # 弹出保存对话框
            dialog = ClipboardSaveDialog(
                self.root,
                content,
                self.item_types,
                self._on_save_clipboard_item_from_dialog
            )
        else:
            # 保存最后一条剪贴板内容，但不弹出对话框
            self.last_clipboard_content = content

    def _on_save_clipboard_item_from_dialog(self, item: ClipboardItem):
        """从对话框保存剪贴板项目回调

        Args:
            item: 要保存的项目
        """
        # 检查是否有新的项目类型
        if item.item_type not in self.item_types:
            self.item_types.append(item.item_type)
            self.item_types.sort()
            # 更新类型列表UI
            self._update_type_list()
        
        # 在主线程中使用主数据库连接
        item_id = self.db_manager.add_item(item)
        
        # 更新UI
        self._load_data()
        
        # 不再显示确认对话框
        # messagebox.showinfo("提示", "内容已保存")

    def _on_card_click(self, item: ClipboardItem):
        """卡片点击回调

        Args:
            item: 被点击的项目
        """
        # 检查是否有变量需要替换
        variables = extract_variables(item.content)
        
        if variables:
            # 如果有变量输入框的值，尝试解析为变量字典
            variable_value = self.variable_var.get().strip()
            variable_dict = {}
            
            if variable_value:
                # 简单处理：假设只有一个变量，直接使用输入框的值
                variable_dict = {variables[0]: variable_value}
            
            # 如果有多个变量，弹出变量输入对话框
            if len(variables) > 1 or not variable_value:
                dialog = VariableInputDialog(
                    self.root,
                    variables,
                    lambda values: self._copy_with_variables(item, values)
                )
                return
            
            # 替换变量并复制
            self._copy_with_variables(item, variable_dict)
        else:
            # 直接复制内容
            copy_to_clipboard(item.content)
            
            # 更新使用次数
            self.db_manager.increment_usage_count(item.id)
            
            # 更新UI
            self._load_data()

    def _copy_with_variables(self, item: ClipboardItem, variables: Dict[str, str]):
        """替换变量并复制内容

        Args:
            item: 项目对象
            variables: 变量字典
        """
        # 替换变量
        content = replace_variables(item.content, variables)
        
        # 复制到剪贴板
        copy_to_clipboard(content)
        
        # 更新使用次数
        self.db_manager.increment_usage_count(item.id)
        
        # 更新UI
        self._load_data()

    def _on_card_right_click(self, item: ClipboardItem, event: tk.Event):
        """卡片右键点击回调

        Args:
            item: 被点击的项目
            event: 事件对象
        """
        # 创建右键菜单
        menu = tk.Menu(self.root, tearoff=0)
        menu.add_command(label="复制", command=lambda: self._on_card_click(item))
        menu.add_command(label="编辑", command=lambda: self._on_edit_item(item))
        menu.add_command(label="删除", command=lambda: self._on_delete_item(item))
        
        # 显示菜单
        menu.post(event.x_root, event.y_root)

    def _on_edit_item(self, item: ClipboardItem):
        """编辑项目

        Args:
            item: 要编辑的项目
        """
        dialog = AddEditDialog(
            self.root,
            item,
            self.item_types,
            self._on_update_item
        )

    def _on_update_item(self, item: ClipboardItem):
        """更新项目回调

        Args:
            item: 更新后的项目
        """
        # 检查是否有新的项目类型
        if item.item_type not in self.item_types:
            self.item_types.append(item.item_type)
            self.item_types.sort()
            # 更新类型列表UI
            self._update_type_list()
        
        # 更新数据库
        self.db_manager.update_item(item)
        
        # 更新UI
        self._load_data()

    def _on_delete_item(self, item: ClipboardItem):
        """删除项目

        Args:
            item: 要删除的项目
        """
        # 确认删除
        if messagebox.askyesno("确认", f'确定要删除"{item.title}"吗？'):
            # 从数据库删除
            self.db_manager.delete_item(item.id)
            
            # 更新UI
            self._load_data()

    def _on_add_click(self):
        """添加按钮点击事件"""
        dialog = AddEditDialog(
            self.root,
            None,
            self.item_types,
            self._on_save_clipboard_item_from_dialog
        )
    
    def _on_clear_click(self):
        """清空按钮点击事件，清空当前显示的卡片并从数据库删除相应数据"""
        # 获取当前显示的卡片列表
        current_items = self.card_view.items
        
        if not current_items:
            messagebox.showinfo("提示", "当前没有显示任何卡片。")
            return
            
        # 计算数量
        item_count = len(current_items)
        
        # 确认对话框
        if messagebox.askyesno("确认删除", 
                          f"确定要删除当前显示的所有卡片吗？\n这将永久删除数据库中的{item_count}条数据，此操作无法撤销。"):
            try:
                # 从数据库中删除这些项目
                deleted_count = 0
                for item in current_items:
                    if item.id:  # 确保项目有ID
                        success = self.db_manager.delete_item(item.id)
                        if success:
                            deleted_count += 1
                
                # 更新卡片视图，传入空列表
                self.card_view.update_cards([])
                
                # 重新获取数据库中的类型列表
                db_types = self.db_manager.get_item_types()
                # 更新类型列表
                self.item_types = sorted(db_types) if db_types else []
                # 刷新类型列表UI
                self._update_type_list()
                
                # 提示用户操作成功
                messagebox.showinfo("操作成功", f"已成功删除{deleted_count}张卡片。")
            except Exception as e:
                messagebox.showerror("操作失败", f"删除过程中发生错误：{str(e)}")
    
    def _on_type_selected(self, event):
        """类型选择事件

        Args:
            event: 事件对象
        """
        # 获取选中的类型
        selection = self.type_listbox.curselection()
        if selection:
            index = selection[0]
            self.current_filter_type = self.type_listbox.get(index)
            
            # 更新筛选
            self._filter_items()

    def _on_search_changed(self, *args):
        """搜索框内容变化事件"""
        self.current_search_keyword = self.search_var.get().strip()
        
        # 更新筛选
        self._filter_items()

    def _on_import(self):
        """导入数据"""
        # 打开文件对话框
        file_path = filedialog.askopenfilename(
            title="导入数据",
            filetypes=[("JSON文件", "*.json"), ("所有文件", "*.*")]
        )
        
        if not file_path:
            return
        
        try:
            # 读取JSON文件
            with open(file_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            
            # 预处理数据，将日期字符串转换为datetime对象
            for item in data:
                # 处理last_used字段
                if 'last_used' in item and item['last_used'] and isinstance(item['last_used'], str):
                    try:
                        item['last_used'] = datetime.datetime.strptime(item['last_used'], '%Y-%m-%d %H:%M:%S')
                    except ValueError:
                        # 如果格式不匹配，尝试其他可能的格式
                        try:
                            item['last_used'] = datetime.datetime.fromisoformat(item['last_used'].replace('Z', '+00:00'))
                        except ValueError:
                            item['last_used'] = datetime.datetime.now()  # 如果无法解析，使用当前时间
                
                # 处理created_at字段
                if 'created_at' in item and item['created_at'] and isinstance(item['created_at'], str):
                    try:
                        item['created_at'] = datetime.datetime.strptime(item['created_at'], '%Y-%m-%d %H:%M:%S')
                    except ValueError:
                        # 如果格式不匹配，尝试其他可能的格式
                        try:
                            item['created_at'] = datetime.datetime.fromisoformat(item['created_at'].replace('Z', '+00:00'))
                        except ValueError:
                            item['created_at'] = datetime.datetime.now()  # 如果无法解析，使用当前时间
            
            # 导入数据
            count = self.db_manager.import_data(data)
            
            # 更新UI
            self._load_data()
            
            messagebox.showinfo("导入成功", f"成功导入{count}条记录")
        except Exception as e:
            messagebox.showerror("导入失败", f"导入数据时出错：{e}")
            # 打印详细错误信息以便调试
            import traceback
            traceback.print_exc()

    def _on_export(self):
        """导出数据"""
        # 打开文件对话框
        file_path = filedialog.asksaveasfilename(
            title="导出数据",
            defaultextension=".json",
            filetypes=[("JSON文件", "*.json"), ("所有文件", "*.*")]
        )
        
        if not file_path:
            return
        
        try:
            # 导出数据
            data = self.db_manager.export_data()
            
            # 创建自定义的JSON编码器处理datetime对象
            class DateTimeEncoder(json.JSONEncoder):
                def default(self, obj):
                    if isinstance(obj, datetime.datetime):
                        return obj.strftime('%Y-%m-%d %H:%M:%S')  # 将datetime转换为字符串
                    return super().default(obj)
            
            # 写入JSON文件，使用自定义编码器
            with open(file_path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2, cls=DateTimeEncoder)
            
            messagebox.showinfo("导出成功", f"成功导出{len(data)}条记录")
        except Exception as e:
            messagebox.showerror("导出失败", f"导出数据时出错：{e}")
            # 打印详细错误信息以便调试
            import traceback
            traceback.print_exc()

    def _show_about(self):
        """显示关于对话框"""
        messagebox.showinfo(
            "关于PromptPaste",
            "PromptPaste 1.0.0\n\n"
            "一款功能强大的剪贴板管理工具，专为存储和管理AI提示词、常用话术而设计。\n\n"
            "© 2025 PromptPaste"
        )

    def _on_close(self):
        """窗口关闭事件处理"""
        # 停止剪贴板监听
        if hasattr(self, 'clipboard_monitor'):
            self.clipboard_monitor.stop()
        # 销毁窗口
        self.root.destroy()
    
    def _on_focus_in(self, event):
        """窗口获得焦点事件处理"""
        self.is_foreground = True
        # 仅当监听功能开启时，处理之前保存的剪贴板内容
        if self.clipboard_monitor_enabled:
            # 如果有最后一条未处理的剪贴板内容，并且与当前剪贴板内容相同，弹出保存对话框
            if hasattr(self, 'last_clipboard_content') and self.last_clipboard_content:
                import pyperclip
                current_content = pyperclip.paste()
                if current_content == self.last_clipboard_content:
                    dialog = ClipboardSaveDialog(
                        self.root,
                        self.last_clipboard_content,
                        self.item_types,
                        self._on_save_clipboard_item_from_dialog
                    )
                    # 清除最后一条内容
                    self.last_clipboard_content = None
    
    def _on_focus_out(self, event):
        """窗口失去焦点事件处理"""
        self.is_foreground = False
    
    def _on_map(self, event):
        """窗口显示事件处理"""
        # 确保窗口在前台显示
        self.root.attributes('-topmost', True)
        self.root.update()
        self.root.attributes('-topmost', False)
        self.is_foreground = True
        
    def _on_unmap(self, event):
        """窗口隐藏事件处理"""
        # 防止窗口被完全隐藏
        if event.widget == self.root:
            self.is_foreground = False
    
    def run(self):
        """运行应用"""
        # 确保窗口显示在前台
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()
        
        # 开始主循环
        self.root.mainloop()
        
        # 关闭剪贴板监听
        self.clipboard_monitor.stop()
        
        # 关闭数据库连接
        self.db_manager.close() 