# NewToys 规则集预设 —— Sitemap 全站链接（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: sitemap
# role: entry
# desc: 抓 sitemap.xml 里的全部网址（标题暂用网址本身）
# category: doc
# artifact: text
# works: 有 sitemap.xml 的站点（用来拿全站链接清单）

# 🔴 XML 带默认命名空间 ⇒ local-name() 匹配；sitemap 没有标题字段 ⇒ title 列暂用 loc 文本。
# date 取 lastmod（多数站点不写，会少一列）。
RULES = [
    ("title", "xpath://*[local-name()='url']/*[local-name()='loc']||//*[local-name()='sitemap']/*[local-name()='loc']"),
    ("url",   "xpath://*[local-name()='url']/*[local-name()='loc']||//*[local-name()='sitemap']/*[local-name()='loc']"),
    ("date",  "xpath://*[local-name()='url']/*[local-name()='lastmod']"),
]
