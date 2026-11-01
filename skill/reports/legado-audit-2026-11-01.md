# legado 书源兼容度审计

- 源总数：**383**（启用 381）
- 现在可跑（纯选择器，三段全 A）：**258/383 (67.4%)**
- JS 兼容层完成后可跑（三段全 ≤B）：**369/383 (96.3%)**
- 补齐未知 API 后上限（三段全 ≤C）：**369/383 (96.3%)**
- 无论如何跑不通（含 Android/WebView/Packages）：**14/383 (3.7%)**

## 按最差段分档

口径：只看搜索 / 目录 / 正文三段，段内取最差档。

| 档 | 含义 | 条数 |
|---|---|---|
| A | 纯选择器 | 258 |
| B | JS+已知API | 111 |
| C | JS+未知API | 0 |
| D | Android专有 | 1 |
| X | Packages/WebView | 13 |

> 另有 21 条源「搜索/目录/正文」全 A，但发现页或详情页用到 JS —— 不影响读，只影响发现/详情。

## 分段通过情况

| 段 | A | B | C | D | X |
|---|---|---|---|---|---|
| 搜索 | 304 | 67 | 0 | 1 | 7 |
| 目录 | 326 | 52 | 0 | 0 | 5 |
| 正文 | 353 | 27 | 0 | 0 | 2 |
| 详情 | 338 | 32 | 0 | 0 | 3 |
| 发现 | 311 | 24 | 0 | 0 | 7 |

## 依赖的 java.* API（L1 施工清单，共 20 个）

| API | 源数 | 判定 |
|---|---|---|
| `java.ajax` | 33 | 可实现 |
| `java.log` | 14 | 可实现 |
| `java.getstring` | 12 | 可实现 |
| `java.timeformat` | 12 | 可实现 |
| `java.put` | 12 | 可实现 |
| `java.get` | 11 | 可实现 |
| `java.toast` | 8 | 可实现 |
| `java.getelements` | 6 | 可实现 |
| `java.tonumchapter` | 5 | 可实现 |
| `java.getelement` | 3 | 可实现 |
| `java.post` | 3 | 可实现 |
| `java.aesbase64decodetostring` | 2 | 可实现 |
| `java.md5encode` | 2 | 可实现 |
| `java.webview` | 2 | 不可实现 |
| `java.connect` | 2 | 可实现 |
| `java.base64decode` | 2 | 可实现 |
| `java.startbrowserawait` | 1 | 不可实现 |
| `java.longtoast` | 1 | 可实现 |
| `java.encodeuri` | 1 | 可实现 |
| `java.t2s` | 1 | 可实现 |

## Android 专有 API（桌面端不可等价）

| API | 出现源数 |
|---|---|
| `java.webview` | 2 |
| `java.startbrowserawait` | 1 |

## 书源类型分布

| 类型 | 条数 |
|---|---|
| 文本 | 369 |
| 图片 | 14 |

## 非 A 档明细

B = 等 JS 兼容层；D/X = 不可实现。按严重度排序。

| 源 | 搜索 | 目录 | 正文 | 原因 |
|---|---|---|---|---|
| 车群小说（优） | X | A | A | 搜索: Packages/WebView；详情: JS（未调 java.*） |
| 中文看书（优） | X | A | B | 正文: JS: java.log；发现: JS（未调 java.*）；搜索: Packages/WebView |
| 🎉 一米小说 | X | A | B | 正文: JS（未调 java.*）；搜索: Packages/WebView |
| 👍 武道文学 | A | X | A | 目录: Packages/WebView |
| 云中书库 | X | A | B | 正文: JS（未调 java.*）；搜索: Packages/WebView |
| 武道文学🎃 | A | X | A | 目录: Packages/WebView |
| 果文学网@游客 | X | A | A | 搜索: Packages/WebView |
| 🐰兔九三🐰 | X | A | B | 正文: JS: java.getelement；搜索: Packages/WebView |
| ⭐️明月小说 | B | X | B | 正文: JS: java.getelements；发现: Packages/WebView；搜索: JS: java.ajax, java.get, java.put, java.toast；目录: Packages/WebView |
| ⭐️星空小说 | A | X | B | 正文: JS: java.getelements；发现: Packages/WebView；目录: Packages/WebView |
| 🎉 星空小说 | A | X | B | 正文: JS: java.getelements；目录: Packages/WebView |
| ️顶点小说️ | X | B | X | 搜索: Packages/WebView；正文: Packages/WebView；目录: JS（未调 java.*） |
| 读万卷info | A | A | X | 正文: Packages/WebView |
| 二八看书（优++） | D | A | A | 搜索: Android: java.startbrowserawait, java.webview |
| 磨铁中文 | B | A | A | 搜索: JS: java.getstring, java.timeformat |
| 苦瓜书盘（导） | B | A | A | 搜索: JS: java.ajax |
| 超星网站（优+） | A | B | A | 发现: JS（未调 java.*）；目录: JS（未调 java.*） |
| 话本小说（优++） | B | B | A | 搜索: JS: java.getstring, java.log；目录: JS: java.timeformat |
| 酷我小说 | A | B | A | 目录: JS（未调 java.*）；发现: JS: java.ajax |
| 轻之文库（优++） | A | B | A | 目录: JS（未调 java.*） |
| 花生小说（优） | A | B | A | 目录: JS（未调 java.*） |
| 百度小说 | A | B | A | 详情: JS: java.get；目录: JS（未调 java.*） |
| 酷我小说 | A | B | A | 发现: JS: java.ajax；目录: JS（未调 java.*） |
| 海词词典（优） | B | B | A | 搜索: JS（未调 java.*）；目录: JS（未调 java.*） |
| 酸奶漫画 | A | B | B | 正文: JS（未调 java.*）；目录: JS（未调 java.*） |
| 海词木稽（优） | B | B | A | 搜索: JS（未调 java.*）；目录: JS（未调 java.*） |
| 全免漫画（优） | B | B | B | 正文: JS: java.aesbase64decodetostring, java.getstring；搜索: JS: java.getstring, java.md5encode, java.timeformat；详情: JS: java.aesbase64decodetostring, java.getstring, java.timeformat；目录: JS: java.aesbase64decodetostring, java.getstring, java.md5encode；发现: JS（未调 java.*） |
| 白浅小说（优） | B | A | A | 搜索: JS（未调 java.*） |
| 神凑轻说（优++） | B | A | A | 发现: JS（未调 java.*）；搜索: JS（未调 java.*） |
| 轻之文库（优++） | A | B | A | 目录: JS（未调 java.*） |
| 番茄小说（优+） | B | A | A | 搜索: JS: java.ajax, java.log |
| 古诗文网（优+） | B | A | A | 搜索: JS（未调 java.*） |
| 古诗词网（优+） | B | A | A | 发现: JS（未调 java.*）；搜索: JS（未调 java.*） |
| 狸猫故事（优+） | B | B | B | 正文: JS: java.getstring；搜索: JS（未调 java.*）；详情: JS: java.getstring；目录: JS: java.getelements, java.toast |
| 轻之文库（优+） | A | B | A | 目录: JS（未调 java.*） |
| 炫动小说（优+） | B | A | A | 搜索: JS: java.ajax, java.longtoast |
| 咸鱼小说（优+） | A | B | A | 目录: JS（未调 java.*） |
| 图书迷子（优） | B | A | A | 搜索: JS: java.connect |
| 零零小说（优） | B | A | A | 发现: JS（未调 java.*）；搜索: JS（未调 java.*） |
| 霹雳书坊（优） | B | A | A | 搜索: JS（未调 java.*） |
| 零点看书（优） | B | A | A | 搜索: JS: java.ajax, java.log |
| 英语阅读（英） | A | B | A | 目录: JS（未调 java.*） |
| 永远漫画（优） | B | A | A | 搜索: JS（未调 java.*） |
| 收费漫画 | A | A | B | 正文: JS: java.ajax |
| 星际漫画（优） | A | A | B | 正文: JS（未调 java.*） |
| 治能寄算（优+） | B | B | B | 正文: JS（未调 java.*）；搜索: JS: java.ajax, java.get, java.put；详情: JS: java.get；目录: JS（未调 java.*） |
| 天域小说网🎃#2 | B | A | A | 搜索: JS（未调 java.*） |
| 📖 追光阅读 | A | A | B | 正文: JS（未调 java.*） |
| ✐ 漫漫漫画 #Haxc1107 | A | B | A | 详情: JS: java.put；目录: JS: java.get, java.put |
| 🏵穿越小说 | A | B | A | 目录: JS（未调 java.*） |
| 文墨中文 | B | A | A | 搜索: JS（未调 java.*） |
| 幻想姬 | B | A | A | 搜索: JS（未调 java.*） |
| 天域小说网tycqzw | B | A | A | 搜索: JS（未调 java.*） |
| 🎉五六中文网 | B | A | A | 搜索: JS（未调 java.*） |
| 👍 趣书网/qubook | B | B | A | 搜索: JS: java.connect, java.get, java.log, java.put；目录: JS: java.getelement |
| 🔰 4020电子书·叁 | B | A | A | 搜索: JS（未调 java.*） |
| 蚂蚁阅读 | B | A | A | 搜索: JS（未调 java.*） |
| 💠 八一中文 | B | A | A | 搜索: JS（未调 java.*） |
| 🍊宝来小说网[自写] | B | A | A | 搜索: JS: java.post；详情: Packages/WebView |
| 🎉蚂蚁阅读① | B | A | A | 搜索: JS（未调 java.*） |
| …共 125 条 | | | | |
