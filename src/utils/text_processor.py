"""
文本处理工具
"""
import re
from typing import Dict, Optional


def replace_variables(text: str, variables: Optional[Dict[str, str]] = None) -> str:
    """替换文本中的变量

    变量格式为 {{变量名}}，将被替换为variables字典中对应的值

    Args:
        text: 原始文本
        variables: 变量字典，键为变量名，值为替换值

    Returns:
        替换后的文本
    """
    if not variables:
        return text

    # 使用正则表达式查找所有变量
    pattern = r"{{(.*?)}}"
    
    def replace_match(match):
        var_name = match.group(1).strip()
        return variables.get(var_name, match.group(0))
    
    # 替换所有匹配的变量
    return re.sub(pattern, replace_match, text)


def extract_variables(text: str) -> list:
    """从文本中提取变量名列表

    Args:
        text: 原始文本

    Returns:
        变量名列表
    """
    pattern = r"{{(.*?)}}"
    matches = re.findall(pattern, text)
    # 去除重复并排序
    return sorted(list(set(match.strip() for match in matches)))


def truncate_text(text: str, max_length: int = 50, suffix: str = "...") -> str:
    """截断文本，超过最大长度时添加后缀

    Args:
        text: 原始文本
        max_length: 最大长度
        suffix: 后缀

    Returns:
        截断后的文本
    """
    if len(text) <= max_length:
        return text
    return text[:max_length - len(suffix)] + suffix 