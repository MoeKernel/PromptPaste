"""
应用程序初始化工具
"""
import os
import json
from pathlib import Path
from typing import List, Dict, Any

from ..database.models import ClipboardItem


def ensure_app_dirs():
    """确保应用程序目录存在"""
    # 创建数据目录
    data_dir = Path.home() / ".promptpaste"
    data_dir.mkdir(exist_ok=True)
    
    # 创建备份目录
    backup_dir = data_dir / "backups"
    backup_dir.mkdir(exist_ok=True)
    
    return str(data_dir)


def create_sample_data(db_path: str) -> List[ClipboardItem]:
    """创建示例数据

    Args:
        db_path: 数据库路径

    Returns:
        示例数据列表
    """
    # 如果数据库已存在，则不创建示例数据
    if os.path.exists(db_path):
        return []
    
    # 创建示例数据
    samples = [
        ClipboardItem(
            title="AI提示词示例",
            content="你是一名专业的{{领域}}专家，请帮我{{任务}}。",
            item_type="AI提示词"
        ),
        ClipboardItem(
            title="商务邮件模板",
            content="尊敬的{{收件人}}：\n\n感谢您对我们公司的关注。关于您提到的{{问题}}，我们将尽快处理。\n\n此致\n敬礼\n{{发件人}}",
            item_type="商务话术"
        ),
        ClipboardItem(
            title="会议记录模板",
            content="会议主题：{{主题}}\n时间：{{时间}}\n参会人员：{{人员}}\n\n会议内容：\n1. \n2. \n3. \n\n下一步计划：\n1. \n2. \n\n",
            item_type="其他"
        )
    ]
    
    return samples


def backup_database(db_path: str, backup_dir: str) -> bool:
    """备份数据库

    Args:
        db_path: 数据库路径
        backup_dir: 备份目录

    Returns:
        是否成功备份
    """
    try:
        if not os.path.exists(db_path):
            return False
        
        # 创建备份文件名
        from datetime import datetime
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_path = os.path.join(backup_dir, f"promptpaste_backup_{timestamp}.db")
        
        # 复制数据库文件
        import shutil
        shutil.copy2(db_path, backup_path)
        
        return True
    except Exception as e:
        print(f"备份数据库失败: {e}")
        return False 