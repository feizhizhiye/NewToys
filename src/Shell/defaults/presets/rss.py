# NewToys 规则集预设 —— RSS / Atom 订阅源（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: rss
# role: entry
# desc: 抓 RSS 2.0 / Atom 订阅源的条目（标题 + 链接 + 日期）
# category: doc
# artifact: text
# works: 任何提供 RSS / Atom 的站点（博客、新闻、多数论坛）

# 🔴 XML 带默认命名空间 ⇒ 用 local-name() 匹配（⛔ 不写 //entry 这类裸路径）；
# 🔴 || 的后续候选省写了前缀，继承前一个的 xpath:。
# 条目输出含 url 字段（段间契约）；日期字段没有的源会自动少一列，不影响使用。
RULES = [
    ("title", "xpath://*[local-name()='item']/*[local-name()='title']||//*[local-name()='entry']/*[local-name()='title']"),
    ("url",   "xpath://*[local-name()='item']/*[local-name()='link']||//*[local-name()='entry']/*[local-name()='link']@href||//*[local-name()='item']/*[local-name()='guid']"),
    ("date",  "xpath://*[local-name()='item']/*[local-name()='pubDate']||//*[local-name()='entry']/*[local-name()='updated']||//*[local-name()='entry']/*[local-name()='published']"),
]
