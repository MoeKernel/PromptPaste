"""
剪贴板监听工具
"""
import threading
import time
from typing import Callable, Optional

import pyperclip
from ..database.db_manager import DatabaseManager

# 全局变量，用于标记是否为内部复制
_is_internal_copy = False


class ClipboardMonitor:
    """剪贴板监听器"""

    def __init__(self, callback: Callable[[str, DatabaseManager], None], check_interval: float = 0.5):
        """初始化剪贴板监听器

        Args:
            callback: 剪贴板内容变化时的回调函数
            check_interval: 检查间隔时间（秒）
        """
        self.callback = callback
        self.check_interval = check_interval
        self.previous_content = ""
        self.running = False
        self.monitor_thread = None

    def start(self) -> None:
        """开始监听剪贴板"""
        if self.running:
            return

        self.running = True
        self.previous_content = pyperclip.paste()
        self.monitor_thread = threading.Thread(target=self._monitor_clipboard, daemon=True)
        self.monitor_thread.start()

    def stop(self) -> None:
        """停止监听剪贴板"""
        self.running = False
        if self.monitor_thread:
            self.monitor_thread.join(timeout=1.0)
            self.monitor_thread = None

    def _monitor_clipboard(self) -> None:
        """监听剪贴板内容变化"""
        global _is_internal_copy
        while self.running:
            try:
                current_content = pyperclip.paste()
                if current_content != self.previous_content and current_content.strip():
                    self.previous_content = current_content
                    if not _is_internal_copy:
                        db_manager = DatabaseManager()
                        self.callback(current_content, db_manager)
                    _is_internal_copy = False  # 重置标志
            except Exception as e:
                print(f"剪贴板监听错误: {e}")
            
            time.sleep(self.check_interval)


def copy_to_clipboard(text: str, monitor: Optional[ClipboardMonitor] = None) -> bool:
    """复制文本到剪贴板

    Args:
        text: 要复制的文本
        monitor: 剪贴板监听器实例，可选

    Returns:
        是否成功复制
    """
    global _is_internal_copy
    try:
        # 先设置标志，再复制内容
        _is_internal_copy = True
        pyperclip.copy(text)
        return True
    except Exception as e:
        print(f"复制到剪贴板错误: {e}")
        return False 