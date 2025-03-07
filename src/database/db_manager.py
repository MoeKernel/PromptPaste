"""
数据库管理模块
"""
import os
import sqlite3
from datetime import datetime
from typing import List, Optional, Dict, Any
import threading

from .models import ClipboardItem


class DatabaseManager:
    """数据库管理类"""

    def __init__(self, db_path: str = "promptpaste.db"):
        """初始化数据库管理器

        Args:
            db_path: 数据库文件路径
        """
        self.db_path = db_path
        self.conn = None
        self.cursor = None
        self.connect()
        self.create_tables()

    def connect(self) -> None:
        """连接数据库"""
        # 检查当前线程是否已经有连接
        thread_id = threading.get_ident()
        if hasattr(self, '_thread_id') and self._thread_id != thread_id:
            # 如果在不同线程中，关闭旧连接并创建新连接
            self.close()
        
        # 创建新连接
        self.conn = sqlite3.connect(self.db_path, detect_types=sqlite3.PARSE_DECLTYPES)
        self.conn.row_factory = sqlite3.Row
        self.cursor = self.conn.cursor()
        self._thread_id = thread_id

    def close(self) -> None:
        """关闭数据库连接"""
        if self.conn:
            self.conn.close()

    def create_tables(self) -> None:
        """创建数据表"""
        self.cursor.execute('''
        CREATE TABLE IF NOT EXISTS clipboard_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            content TEXT NOT NULL,
            item_type TEXT NOT NULL,
            usage_count INTEGER DEFAULT 0,
            last_used TIMESTAMP,
            created_at TIMESTAMP
        )
        ''')
        self.conn.commit()

    def add_item(self, item: ClipboardItem) -> int:
        """添加剪贴板项目

        Args:
            item: 剪贴板项目对象

        Returns:
            新添加项目的ID
        """
        self.cursor.execute('''
        INSERT INTO clipboard_items (title, content, item_type, usage_count, last_used, created_at)
        VALUES (?, ?, ?, ?, ?, ?)
        ''', (
            item.title,
            item.content,
            item.item_type,
            item.usage_count,
            item.last_used,
            item.created_at
        ))
        self.conn.commit()
        return self.cursor.lastrowid

    def get_item(self, item_id: int) -> Optional[ClipboardItem]:
        """获取指定ID的剪贴板项目

        Args:
            item_id: 项目ID

        Returns:
            剪贴板项目对象，如果不存在则返回None
        """
        self.cursor.execute('SELECT * FROM clipboard_items WHERE id = ?', (item_id,))
        row = self.cursor.fetchone()
        if row:
            return self._row_to_item(row)
        return None

    def get_all_items(self) -> List[ClipboardItem]:
        """获取所有剪贴板项目

        Returns:
            剪贴板项目列表
        """
        self.cursor.execute('SELECT * FROM clipboard_items ORDER BY last_used DESC')
        rows = self.cursor.fetchall()
        return [self._row_to_item(row) for row in rows]

    def get_items_by_type(self, item_type: str) -> List[ClipboardItem]:
        """获取指定类型的剪贴板项目

        Args:
            item_type: 项目类型

        Returns:
            剪贴板项目列表
        """
        self.cursor.execute('SELECT * FROM clipboard_items WHERE item_type = ? ORDER BY last_used DESC', (item_type,))
        rows = self.cursor.fetchall()
        return [self._row_to_item(row) for row in rows]

    def search_items(self, keyword: str) -> List[ClipboardItem]:
        """搜索剪贴板项目

        Args:
            keyword: 搜索关键词

        Returns:
            匹配的剪贴板项目列表
        """
        search_term = f"%{keyword}%"
        self.cursor.execute('''
        SELECT * FROM clipboard_items 
        WHERE title LIKE ? OR content LIKE ? 
        ORDER BY last_used DESC
        ''', (search_term, search_term))
        rows = self.cursor.fetchall()
        return [self._row_to_item(row) for row in rows]

    def update_item(self, item: ClipboardItem) -> bool:
        """更新剪贴板项目

        Args:
            item: 剪贴板项目对象

        Returns:
            更新是否成功
        """
        if item.id is None:
            return False

        self.cursor.execute('''
        UPDATE clipboard_items 
        SET title = ?, content = ?, item_type = ?, usage_count = ?, last_used = ?
        WHERE id = ?
        ''', (
            item.title,
            item.content,
            item.item_type,
            item.usage_count,
            item.last_used,
            item.id
        ))
        self.conn.commit()
        return self.cursor.rowcount > 0

    def delete_item(self, item_id: int) -> bool:
        """删除剪贴板项目

        Args:
            item_id: 项目ID

        Returns:
            删除是否成功
        """
        self.cursor.execute('DELETE FROM clipboard_items WHERE id = ?', (item_id,))
        self.conn.commit()
        return self.cursor.rowcount > 0

    def increment_usage_count(self, item_id: int) -> bool:
        """增加项目使用次数并更新最后使用时间

        Args:
            item_id: 项目ID

        Returns:
            更新是否成功
        """
        self.cursor.execute('''
        UPDATE clipboard_items 
        SET usage_count = usage_count + 1, last_used = ?
        WHERE id = ?
        ''', (datetime.now(), item_id))
        self.conn.commit()
        return self.cursor.rowcount > 0

    def get_item_types(self) -> List[str]:
        """获取所有项目类型

        Returns:
            项目类型列表
        """
        self.cursor.execute('SELECT DISTINCT item_type FROM clipboard_items')
        rows = self.cursor.fetchall()
        return [row[0] for row in rows]

    def export_data(self) -> List[Dict[str, Any]]:
        """导出数据为字典列表

        Returns:
            数据字典列表
        """
        self.cursor.execute('SELECT * FROM clipboard_items')
        rows = self.cursor.fetchall()
        return [dict(row) for row in rows]

    def import_data(self, data: List[Dict[str, Any]]) -> int:
        """导入数据

        Args:
            data: 数据字典列表

        Returns:
            导入的记录数
        """
        count = 0
        for item_dict in data:
            # 确保不导入ID，让数据库自动生成
            if 'id' in item_dict:
                del item_dict['id']
            
            # 转换为ClipboardItem对象
            item = ClipboardItem(
                title=item_dict.get('title', ''),
                content=item_dict.get('content', ''),
                item_type=item_dict.get('item_type', 'AI提示词'),
                usage_count=item_dict.get('usage_count', 0),
                last_used=item_dict.get('last_used'),
                created_at=item_dict.get('created_at')
            )
            
            # 添加到数据库
            self.add_item(item)
            count += 1
            
        return count

    def _row_to_item(self, row: sqlite3.Row) -> ClipboardItem:
        """将数据库行转换为ClipboardItem对象

        Args:
            row: 数据库行

        Returns:
            ClipboardItem对象
        """
        return ClipboardItem(
            id=row['id'],
            title=row['title'],
            content=row['content'],
            item_type=row['item_type'],
            usage_count=row['usage_count'],
            last_used=row['last_used'],
            created_at=row['created_at']
        ) 