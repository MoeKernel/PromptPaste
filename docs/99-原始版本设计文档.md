# PromptPaste — 原始 Python 版设计文档

> 基于 Python/Tkinter 版本逆向整理，2025-03

---

## 1. 应用概述

PromptPaste 是一个剪贴板管理工具，核心功能：

- 监听系统剪贴板变化，自动弹出保存对话框
- 以卡片网格形式展示已保存内容
- 支持 `{{变量}}` 模板替换
- 分类筛选 + 实时搜索
- 使用次数统计
- JSON 导入/导出

---

## 2. 数据模型

### ClipboardItem

| 字段 | 类型 | SQLite | 默认值 | 说明 |
|------|------|--------|--------|------|
| id | int | INTEGER PK AUTOINCREMENT | 自增 | 主键 |
| title | string | TEXT NOT NULL | "" | 标题 |
| content | string | TEXT NOT NULL | "" | 完整内容 |
| item_type | string | TEXT NOT NULL | "AI提示词" | 分类标签 |
| usage_count | int | INTEGER DEFAULT 0 | 0 | 复制次数 |
| last_used | datetime | TIMESTAMP | null | 最后使用时间 |
| created_at | datetime | TIMESTAMP | now() | 创建时间 |

### SQLite DDL

```sql
CREATE TABLE IF NOT EXISTS clipboard_items (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT NOT NULL,
    content     TEXT NOT NULL,
    item_type   TEXT NOT NULL,
    usage_count INTEGER DEFAULT 0,
    last_used   TIMESTAMP,
    created_at  TIMESTAMP
);
```

---

## 3. CRUD 接口

| 操作 | SQL | 参数 |
|------|-----|------|
| 查全部 | `SELECT * ORDER BY last_used DESC` | — |
| 按类型 | `SELECT * WHERE item_type=? ORDER BY last_used DESC` | item_type |
| 搜索 | `SELECT * WHERE title LIKE ? OR content LIKE ? ORDER BY last_used DESC` | `%keyword%` |
| 查单个 | `SELECT * WHERE id=?` | id |
| 新增 | `INSERT INTO ...` | ClipboardItem |
| 更新 | `UPDATE SET title=?,content=?,item_type=?,usage_count=?,last_used=? WHERE id=?` | ClipboardItem |
| 删除 | `DELETE WHERE id=?` | id |
| 计数+1 | `UPDATE SET usage_count+=1, last_used=now WHERE id=?` | id |
| 查全部类型 | `SELECT DISTINCT item_type ORDER BY item_type` | — |

---

## 4. 界面布局

### 主窗口 (915×580, 最小780×480)

```
┌──────────────────────────────────────────────────┐
│  菜单栏: [文件 ▼] [帮助 ▼]                        │
├──────────┬───────────────────────────────────────┤
│          │  [+添加] [☑自动弹窗] 搜索:[    ] 替换:[    ] [清空]│
│  分类     ├───────────────────────────────────────┤
│          │  ┌──────────┐ ┌──────────┐            │
│  ▸全部    │  │ 标题     │ │ 标题     │            │
│  AI提示词 │  │ 类型标签  │ │ 类型标签  │            │
│  商务话术 │  │ ──────── │ │ ──────── │            │
│  其他     │  │ 内容预览  │ │ 内容预览  │            │
│          │  │ 次数  最后 │ │ 次数  最后 │            │
│          │  └──────────┘ └──────────┘            │
│          │  ┌──────────┐ ┌──────────┐            │
│          │  │ ...      │ │ ...      │            │
│          │  └──────────┘ └──────────┘            │
│          │                                       │
│          │           [滚动区域]                    │
├──────────┴───────────────────────────────────────┤
│  底部状态: 共 N 条                                 │
└──────────────────────────────────────────────────┘
```

### 布局参数

| 区域 | 组件 | 参数 |
|------|------|------|
| 侧边栏 | tk.Listbox | 宽度140px, 高度15项 |
| 卡片网格 | 2列, CardView | 卡片宽320px, 间距8px, 内边距8px |
| 卡片本体 | ttk.Frame | 最小高度160px, ridge边框 |
| 类型徽章 | ttk.Frame | 浅蓝背景 #e8f0f8 |
| 搜索框 | ttk.Entry | 宽度30字符 |
| 变量输入 | ttk.Entry | 宽度30字符 |

---

## 5. 交互流程

### 启动
```
main() → 创建 ~/.promptpaste/ → 连接SQLite → 播种示例数据
       → 创建 MainWindow → _create_widgets() → _load_data()
       → clipboard_monitor.start() → root.mainloop()
       → 退出时 backup_database()
```

### 双击卡片
```
_on_card_click(item)
  ├─ extract_variables(content)
  ├─ 无变量 → copy_to_clipboard() → increment_usage_count() → reload
  └─ 有变量 →
       ├─ 单变量 + 输入框有值 → 直接替换 → copy → increment → reload
       └─ 多变量/输入框空 → VariableInputDialog → 同上
```

### 剪贴板监听
```
后台线程每0.5秒:
  pyperclip.paste() vs previous_content
  如果变化且非空且不是内部复制:
    callback(content) → 前台 → ClipboardSaveDialog
```

### 窗口焦点
```
FocusIn  → 如果 last_clipboard_content 匹配当前剪贴板 → 弹出保存对话框
FocusOut → 标记前台=false
Map     → topmost=True 强制置顶
```

---

## 6. 对话框

### AddEditDialog (500×400 模态)
- 标题/添加模式: "添加内容"，编辑模式: "编辑内容"
- 控件: 标题Entry, 类型Combobox(可编辑), 内容Text(带滚动条)
- 提示: "使用 {{变量名}} 格式添加可替换的变量"

### ClipboardSaveDialog (500×400 模态)
- 标题: "保存剪贴板内容"
- 与 AddEditDialog 同布局，内容预填，标题自动生成(前20字符)

### VariableInputDialog (400×300 模态)
- "请为以下变量输入值:" + 每个变量一行输入框
- 确定 → 回调 {变量名: 值} 字典

---

## 7. 样式配置

| 元素 | 样式 |
|------|------|
| 窗口主题 | ttkthemes "arc" |
| 默认背景 | #ffffff |
| 默认文字 | #333333 |
| 卡片边框 | ridge, 1px |
| 按钮悬停 | #e0e0e0 |
| 按钮按下 | #d0d0d0 |
| 选中类型 | 背景 #4a6984, 白色文字 |
| 开关选中 | 文字 #1890ff |
| 字体 | Segoe UI, 10px |

---

## 8. 文件清单 (Python)

| 文件 | 行数 | 职责 |
|------|------|------|
| main.py | 47 | 入口, 初始化, 备份 |
| src/database/models.py | 28 | ClipboardItem 数据类 |
| src/database/db_manager.py | 266 | SQLite CRUD |
| src/ui/main_window.py | 677 | 主窗口编排 |
| src/ui/card_view.py | 227 | 卡片网格组件 |
| src/ui/dialogs.py | 334 | 3个对话框 |
| src/utils/clipboard.py | 84 | 剪贴板监听 |
| src/utils/text_processor.py | 62 | 文本处理 |
| src/utils/init_app.py | 86 | 目录/示例数据/备份 |
| resources/styles/default.py | 98 | ttk 样式配置 |
| **总计** | **~1909** | |

---

## 9. 已知 Python 版的性能问题

1. **Tkinter 卡片渲染卡顿**: 超过 200 张卡片时滚动和双击有明显延迟
2. **剪贴板轮询开销**: 每 0.5 秒 pyperclip.paste() 调用在高频复制场景下增加延迟
3. **全量刷新**: 每次操作都调用 `_load_data()` 重新渲染所有卡片，而非增量更新
4. **线程安全问题**: 后台线程直接操作 UI 对象
