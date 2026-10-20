# NewToys 规则集预设 —— OPDS 电子书目录（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: opds
# role: entry
# desc: 抓 OPDS 书目录的条目（书名 + 详情链接 + 作者）
# category: book
# works: OPDS 电子书目录（Calibre 书库、多数书站的 OPDS 端点）

# OPDS 本质是 Atom；🔴 local-name() 匹配命名空间；每条取第一个链接（通常是 alternate / 获取链接）。
RULES = [
    ("title",  "xpath://*[local-name()='entry']/*[local-name()='title']"),
    ("url",    "xpath://*[local-name()='entry']/*[local-name()='link'][1]@href||//*[local-name()='entry']/*[local-name()='link']@href"),
    ("author", "xpath://*[local-name()='entry']/*[local-name()='author']/*[local-name()='name']"),
]
