"""
数据库模型定义
"""
from dataclasses import dataclass
from datetime import datetime
from typing import Optional


@dataclass
class ClipboardItem:
    """剪贴板项目数据模型"""
    id: Optional[int] = None
    title: str = ""
    content: str = ""
    item_type: str = "AI提示词"  # 默认类型为AI提示词
    usage_count: int = 0
    last_used: Optional[datetime] = None
    created_at: Optional[datetime] = None

    def __post_init__(self):
        """初始化后处理"""
        if self.created_at is None:
            self.created_at = datetime.now()
        if self.last_used is None:
            self.last_used = self.created_at 