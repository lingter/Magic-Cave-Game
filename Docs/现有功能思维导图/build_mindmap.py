"""Build documentation only. Does not import Unity, modify assets, or run the Editor."""
from pathlib import Path
import html
import json
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
RUNTIME = 'Assets/MagicCavePrototype/Runtime/'

def source(file, symbol, prefix=RUNTIME):
    path = prefix + file + '.cs'
    lines = (ROOT / path).read_text(encoding='utf-8-sig').splitlines()
    matches = [i + 1 for i, line in enumerate(lines) if symbol in line]
    if not matches:
        raise ValueError((path, symbol))
    return dict(path=path, line=matches[0], symbol=symbol)

def f(id, name, brief, how, unity, file, symbol):
    return dict(id=id, name=name, brief=brief, how=how, unity=unity,
                sources=[source(file, symbol)])

def s(id, name, role, color, unity, features):
    return dict(id=id, name=name, role=role, color=color, unity=unity, features=features)

systems = [
s('flow','流程与状态','串联城镇、探索、战斗与结算','#305bd6','MonoBehaviour · Update · OnGUI',[
 f('flow-state','游戏状态与面板','主流程状态 + 叠加面板', 'GameScreen 控制主页、探索、战斗、奖励、结算与结果；GamePanel 控制背包、暂停、商店等面板。由 PrototypeGame 的 partial 文件共享状态。','MonoBehaviour；Update 每帧推进；OnGUI 绘制界面；C# enum 管理状态。','PrototypeGame','public enum GameScreen'),
 f('flow-start','开始新局与继续','种子、地貌选择、规则快照','新局先生成地图，再复制设置、应用天赋、校验，成功后才替换旧存档；继续时复制已有本局快照，不重新生成地图。','Debug.Log 输出种子；Resources 间接加载属性配置；JsonUtility 支撑深拷贝。','PrototypeGame','public void StartNewRun(int seed, MapGenerationMode mode)'),
 f('flow-switch','探索与战斗切换','主动交互 → 单敌战斗 → 返回','相邻敌人交互调用 BeginBattle；ProcessBattle 处理胜败，胜利进入选牌或探索，失败进入正式结算。战斗保留本局生命与地图状态。','MonoBehaviour.Update；Time.deltaTime 驱动敌方行动等待；C# Battle 保存战斗状态。','PrototypeGame','public void BeginBattle'),
 f('flow-pause','暂停、回城与离局','手动存档与正式结算分流','面板和流程条件阻止移动或战斗推进；返回门可存档回城或正式离局。暂停退出按未保存提示处理，不自动发放带出奖励。','Input System 的 Escape；Application.Quit；暂停靠状态门控，未通过 Time.timeScale 实现。','PrototypeGame','void SaveAndLeave')]),
s('town','城镇与局外成长','为下一局提供职业、天赋和资源','#976725','IMGUI · Input System · Texture2D',[
 f('town-map','世界地图与战士公会','城镇 / 公会两套格子布局','TownPage、InGuild 管理公会入口、会长、导师、仓库和出发入口；城镇采用自己的格子布局与路径，复用方向输入及轮盘。','Input System；GUI.DrawTexture / GUI.Label 绘制城镇；Vector2.MoveTowards 平滑走格。','PrototypeTown','void DrawTownMap'),
 f('town-talent','三属性天赋星图','前置条件、点亮、回撤返点','力量、敏捷、智慧三区；满足职业、公会、声望、前置和点数条件后点亮即激活。回撤须先处理依赖节点；初始3点、每级1点为试配，正式点数产出未接入。','GUI / Rect / Color 绘制节点；Texture2D 支撑星图；C# TownRules 校验；存储层立即保存。','TownProgression','public static string TalentLock'),
 f('town-guild','公会升级与成长快照','金币 / 声望 → 条件与新局增益','会长提供公会升级；出发时 TownRules.Apply 写入属性与开场能量、格挡、战后回血等效果。城镇后来点亮或回撤，不改写已出发局。','IMGUI 展示条件；JsonUtility 复制本局状态；天赋规则为 C# 逻辑。','TownProgression','public static void Apply'),
 f('town-warehouse','局外仓库与资源处理','存储、出售、捐赠、扩容','物品数量和装备实例分别保存；部分旧物品可出售，古代遗物可捐赠换声望，金币可扩容。新增装备和精华当前仅存储，未开放下局携入。','IMGUI 分页界面；JsonUtility 保存；容量和交易是 C# 规则。','PrototypeTown','void DrawTownWarehouse')]),
s('map','地图生成与布局','为探索提供地形、对象和路线风险','#197a69','Vector2Int · 自定义 C# 生成算法',[
 f('map-terrain','两种地貌与五层流程','房间走廊 / 自然挖掘二选一','整局前4张探索图采用所选方式：房间25×25，自然挖掘31×31；第5张为固定首领图。生成验收失败重试；旧5层或8层存档保留原快照。','Vector2Int 表示格子；Mathf 辅助计算；房间、挖掘及连通验收由 C# 完成。','MapGenerator','static MapData Generate'),
 f('map-random','种子与独立随机流','地图 / 洗牌 / 选牌 / 开箱分流','RandomStream 自行维护状态，按 seed + salt 派生各流；开箱、战斗和选牌互不挤占同一序列。复现要求生成版本、规则和相关操作顺序一致。','[Serializable] 数据；Debug.Log；随机为自定义 C# RandomStream，不依赖 UnityEngine.Random。','PrototypeData','public sealed class RandomStream'),
 f('map-layout','箱袋与镇守入口布局','永久障碍下验证可达性','默认10容器：2装备、5财宝、3消耗品；8受镇守、2无镇守。守卫覆盖原有地面交互入口，数量按入口生成；容器取空仍占格。可破墙绕行，开箱不检查守卫存活。','Vector2Int；Queue / HashSet 的 C# BFS；BoxCollider2D 在显示层提供实体组件。','MapGenerator','public static bool ValidateCombatApproaches'),
 f('map-door','楼梯、商人与安全出生','路径连通、间距与避怪约束','验收永久容器和商人障碍下的地面、楼梯及交互位连通。返回门在第3层和首领胜利后提供；新局怪物等级按楼层。商人位置须八向避怪。','Vector2Int；自定义 C# 路径验收；SpriteRenderer 显示楼梯与对象。','MapGenerator','public static bool ValidateContainerRoutes')]),
s('nav','移动、导航与交互','把键鼠输入转成合法走格和操作','#4b68c2','Input System · Vector2.MoveTowards',[
 f('nav-keys','键盘走格与平滑表现','先按仍按住的方向优先','输入事件保留按键先后；逻辑位置和存档在格心，显示位置在相邻格心间匀速插值，跨格继承剩余移动距离。洞窟3格/秒、城镇4格/秒为当前试配。','InputSystem.onEvent、Keyboard、InputEventPtr；Time.deltaTime；Vector2.MoveTowards。','PrototypeGame','void TrackMovementKeys'),
 f('nav-path','点击寻路与相邻交互','只走已揭示通路；键盘可接管','BFS 避开墙、存活敌人、箱袋和商人；点对象先接近四向交互位再执行。不可达给提示，不自动破墙；合法通行由 MapData.CanTraverse 判定。','Camera.ScreenToWorldPoint 换算格子；Mouse.current；Vector2Int；寻路为 C# BFS。','MapGenerator','public static List<Vector2Int> Path'),
 f('nav-wheel','方向轮盘与大小地图','常驻 / 长按轮盘；点地图寻路','右下轮盘直接拖动；地图长按0.25秒临时呼出。大小地图仅接受已揭示、可通行且可达终点；放大地图阻止底层输入，点击可达地块后收起。','Mouse / Keyboard；Time.unscaledTime；GUI.DrawTexture、Rect；自绘地图与轮盘。','PrototypeNavigation','void ReadNavigationPointer'),
 f('nav-dig','交互、碰撞与破墙','E / 鼠标操作；破墙重建','TargetKind 区分敌人、容器、门、墙和商人。内部墙可一次破坏，边界墙不可破；修改 Tile 后重建墙线和视野。输入由面板和 UI 区域分流，避免点击穿透。','Input System；Camera；BoxCollider2D / PrototypeTarget 组件；通行主判定为 C# 网格检查。','PrototypeGame','void Interact(Target target)')]),
s('sight','视野、火把与情报','决定可发现内容与路线信息','#b57b13','SpriteRenderer · 自定义格子视线',[
 f('sight-los','常规与火把视野','3×3 / 5×5去四顶角','常规最多9格，火把最多21格；Supercover 射线处理墙体遮挡，斜向不能穿墙角；首个遮挡格可见，后方不揭示。首领地图全可见。','Vector2Int / Mathf 辅助；视线算法为 C#，未使用 Unity 2D Light 灯光。','MapGenerator','public static bool HasLineOfSight'),
 f('sight-reveal','永久揭示与迷雾显示','发现后保留；不等于当前视线','MapData.revealed 记录发现；移动、破墙、读档及切图刷新。已发现敌人、容器和门持续显示，小地图读取同一揭示记录。','SpriteRenderer 绘制遮罩；GameObject.SetActive / Renderer.enabled 控制显隐。','PrototypeWorld','partial void RefreshVisibility'),
 f('sight-torch','同一根火把可切换开关','点燃消耗1根；保留剩余耐久','首次点燃取得20步耐久；手动熄灭保留耐久，再点燃同一根不重复消耗库存；耗尽后才需下一根。开关与剩余耐久随本局保存。','GUI / Input System 提供操作；TorchLight 是 C# 状态，未使用计时协程或实时灯光。','RunSystems','public static class TorchLight'),
 f('sight-step','照明与移动计步联动','仅燃烧时的成功走格扣步','MoveGrid 成功后调用 TorchLight.Step，再刷新揭示和显示；碰墙、等待、战斗、交互、破墙不扣步，熄灭期间不扣步。','MonoBehaviour.Update 驱动移动；Vector2Int；计步依赖走格事件，非 Time 秒数。','PrototypeGame','bool MoveGrid')]),
s('items','物品、背包与装备','让探索产出成为可用资源','#9251af','IMGUI · Input System · ScriptableObject',[
 f('items-loot','三类容器与首次开箱','首次有效开启抽取；之后不重抽','装备箱、财宝箱、消耗品袋使用规则快照和 containerRandom；满包仍确定内容，支持部分拾取。新局空箱保持不透明开启外观、碰撞，取消交互。','[Serializable]；Sprite / SpriteRenderer 选择开闭图；随机和拾取为 C# 规则。','ContainerLoot','public static bool Open'),
 f('items-bag','20格背包与拖拽整理','装备1件/格；同类材料最多20','Inventory 管理拾取、堆叠、整格交换和明确丢弃；ItemUnit 保存独立ID及获取顺序，为穿戴、带出和失败排序提供唯一身份。','OnGUI / GUI.DrawTexture 绘制格子；Mouse.current 处理拖拽；Rect 命中检测。','PrototypeItemGrid','void ReadItemDrag'),
 f('items-equip','八个独立装备槽','整件剑盾 + 四护甲 + 三戒指','拖入匹配部位可换装；旧装备退回来源格；卸装检查容量。剑盾只有一个实例，三个戒指分别记录实际槽位；旧木盾仅保留兼容入口。','IMGUI 格子与鼠标拖拽；ScriptableObject 属性配置；装备许可/交换为 C#。','RunSystems','public static string Equip'),
 f('items-quick','消耗品与三格快捷栏','绑定背包类别；不复制库存','探索点击或1/2/3使用，背包页可点击；拾取/购买时有空位自动绑定，支持拖入替换和右键解绑。药剂回血、精华本局加属性、强攻药剂按战斗次数持续；当前无战斗消耗品牌。','Input System 数字键与鼠标；IMGUI 绘制；RunData.quickSlots / TorchLight 保存绑定与火把状态。','RunSystems','public static class QuickBar')]),
s('stats','角色属性与数值','为卡面、预览和结算提供统一结果','#5363ad','ScriptableObject · Resources · JsonUtility',[
 f('stats-template','角色模板与规则快照','固定模板；成长属于本局','PrototypeAttributeSet 加载 CharacterRules，复制进 RunData；CharacterState 保存天赋、穿戴和增益。新局 secondary=None，取消副属性选择及最终伤害加成；旧局保留兼容。','ScriptableObject；[CreateAssetMenu]；Resources.Load；JsonUtility 深拷贝。','PrototypeAttributeSet','public static AttributeRules LoadRules'),
 f('stats-atk','三属性、装备与实际ATK','武器白值 + 三属性修正','Calculate 汇总模板、等级成长、天赋、装备和有效增益；武器上配置力量/敏捷/智慧修正。装备可提供生命、防御和属性；换装重算ATK并更新卡面，不替换卡组。','[Serializable] 配置/状态；纯 C# decimal 计算；未依赖 Unity 物理或动画计算伤害。','CharacterAttributes','public static CharacterStats Calculate'),
 f('stats-buff','等级与增益生命周期','力量/生命成长；按场次结束增益','等级参与主属性与生命成长；精华写入本局永久 modifier，强攻药剂只在战斗中参与并按剩余战斗数衰减。战后回血、结束增益、经验升级按既定顺序执行。','[Serializable] 属性增益列表；C# 事件调用链；不使用 Animator 状态。','CharacterAttributes','public static void FinishBattle'),
 f('stats-damage','伤害、格挡与一致预览','卡牌倍率；逐段减防再扣格挡','攻击取实际ATK×牌倍率，盾击取当前格挡；防御牌按模板读主属性或实际ATK。最终倍率后整数取整；每段先减防御、再扣格挡、再扣生命，预览复用同一计算。','Mathf 用于辅助显示；核心为 C# decimal + RoundDamage / CombatNumbers，防御不是百分比。','CharacterAttributes','public static class CombatNumbers')]),
s('battle','卡牌战斗与本局成长','把探索遭遇转成构筑和资源产出','#bf5365','Update · IMGUI · Input System',[
 f('battle-turn','单敌回合与资源','玩家先手；3能量；每回合抽5','Battle 管理玩家/敌方回合；回合开始清自身格挡并补能抽牌；结束回合弃剩余牌，清战意；抽空洗弃牌。天赋可提供开场额外能量或初始格挡。','MonoBehaviour.Update 驱动敌方等待；Time.deltaTime；牌堆与回合为 C# 状态。','RunSystems','public sealed class Battle'),
 f('battle-play','十类卡牌与牌堆查看','点击出牌；多手牌分页','起始牌为打击、防御、整备，另7类升级候选。Play 校验费用、移出手牌、按序结算、入弃牌堆；攻击对唯一敌人。界面可查看抽牌/弃牌组成。','OnGUI / GUI.Label / GUI.DrawTexture；Mouse.current；自绘按钮点击，而非 uGUI Button。','RunSystems','public bool Play'),
 f('battle-enemy','敌人意图、胜败与首领','洞穴蝠 / 岩甲虫 / 岩洞守卫','三类敌人按各自循环展示意图；等级调整生命和攻击。胜利将该敌人标为 defeated、发经验和牙齿；首领胜利生成返回门和可配置遗物奖励，安全校正玩家站位。','SpriteRenderer 显示敌人；IMGUI 展示意图；C# BattleOutcome / defeated 状态。','RunSystems','public static void FinishVictory'),
 f('battle-growth','经验升级与三选一构筑','经验 → 升级 → 选牌/跳过','胜利普通10、首领30经验；升级曲线20+10×(L-1)，逐次给3种候选，可跳过。新卡写入本局deck；失败经验仅统计，直接结算。等级、卡组不继承新局。','IMGUI 候选卡界面；自定义 RandomStream 洗候选；C# Growth 管理升级。','RunSystems','public static List<CardKind> NextUpgrade')]),
s('economy','牙齿与集齿者','战斗货币回流到探索消耗品','#ad7827','IMGUI · SpriteRenderer · JSON快照',[
 f('economy-teeth','单局牙齿货币','胜利发放；不占背包格','普通怪3、首领10颗为试配；defeated 防止重复发放。牙齿存 RunData，不入仓库、不兑换城镇金币，正式结束后不继承。','[Serializable] RunData；JsonUtility 保存；产出为 C# 规则。','ToothMerchant','public static int VictoryTeeth'),
 f('economy-plan','随机商人分布','每层至多1；连续5层至少2','PlanFloors 按种子安排普通楼层，再补足滑动窗口；首领层计入窗口但不生成商人。位置八向避怪，且不破坏永久障碍下的通路和交互位。','Vector2Int；自定义随机流和 C# 布局验收。','ToothMerchant','public static bool[] PlanFloors'),
 f('economy-buy','消费、容量校验与交易','药剂、火把、强攻药剂','购买前检查范围、开关、余额、背包与堆叠空间；成功后才扣牙齿并发物品，触发快捷栏自动绑定。失败不扣款、不推进物品ID。','IMGUI 商品界面；Input System 点击/E；Inventory.Pickup 复用物品逻辑。','ToothMerchant','public static string Buy'),
 f('economy-bridge','两套经济与资源方向','牙齿用于本局；金币用于局外','探索商店回补本局消耗品；金币和声望由正式结算/局外处理获得，驱动公会与仓库。暂存回城只保存牙齿余额，不发城镇结算奖励。','JsonUtility 保存两类数据；经济换算为 C# TownRules，未自动兑换牙齿。','TownProgression','public static long SettlementGold')]),
s('settings','调参与功能开关','控制随后新局的原型规则','#527784','IMGUI · Input System · JsonUtility',[
 f('settings-ui','主界面设置与草稿','功能 / 卡牌 / 怪物 / 掉落','设置面板使用草稿；支持数值加减、文本输入、粘贴、确认、保存/取消及恢复默认。保存前校验；超出范围或空间不足会拒绝，不替换已有存档。','OnGUI / GUI；Keyboard.onTextInput；GUIUtility.systemCopyBuffer；Rect 点击。','PrototypeSettingsInterface','void DrawSettings'),
 f('settings-rules','十二类功能开关','迷雾、消耗品、装备、镇守等','可切换火把、牙齿、商人、出发天赋、经验、选牌、破墙等。关闭掉落类别仍保留容器，可开空；关闭物品守卫是显式调试选项，默认布局规则继续有效。','[Serializable] PrototypeSettings；IMGUI；具体效果在 C# 系统入口判断。','PrototypeSettings','public sealed class PrototypeSettings'),
 f('settings-balance','卡牌、怪物与掉落调参','费用/倍率/数量/权重/首领奖励','配置初始卡数量、费用、抽牌、倍率和战意；怪物生命、攻击、防御、格挡及自由怪数量；各池权重、数量范围、首领遗物概率。数值是原型试配。','JsonUtility 序列化配置；C# Validate 校验数组、范围和候选池。','PrototypeSettings','public void Validate'),
 f('settings-snapshot','仅随后新局生效','复制设置与掉落池进本局','Apply 写入 settingsVersion=1 与规则副本，后续系统读取 PrototypeSettings.For(run)。继续旧局使用原快照，旧settingsVersion=0沿用原逻辑。','JsonUtility 深拷贝；[Serializable]；Resources 属性配置与本局设置快照并存。','PrototypeSettings','public void Apply')]),
s('save','存档、结算与旧局兼容','持久化全部状态并防止重复奖励','#487a63','JsonUtility · persistentDataPath',[
 f('save-files','三类JSON与原子写入','session / warehouse / settings','默认路径由 Application.persistentDataPath 派生。临时文件写入、Flush后替换；本局、仓库/城镇、设置分别保存。设置损坏暂用默认，保留原文件。','Application.persistentDataPath；JsonUtility.ToJson / FromJson；文件读写用 .NET System.IO。','PrototypeStore','static void AtomicWrite'),
 f('save-checkpoint','返回门存档与继续','保存快照和随机进度','session 保存地图、揭示、墙体、敌人/容器、位置、生命、经验、牌组、背包、装备/属性/天赋、牙齿、火把开关耐久、快捷栏及各随机流。继续不重Roll、不领奖励。','[Serializable] 数据模型；JsonUtility 深拷贝与序列化；不是 PlayerPrefs 存档。','PrototypeStore','public void SaveCheckpoint'),
 f('save-settle','正式带出与防重复入账','主动最多10件；失败最多5件','背包与穿戴装备一起选；失败按品质与获取时间自动取。PendingSettlement 保留待处理流程；run.id / committedRuns 账本防重复发物品、金币和声望。仓库不足则暂停提交。','JsonUtility 保存事务状态；System.IO 原子替换；C# 账本与容量检查。','PrototypeStore','public List<ItemUnit> CommitSettlement'),
 f('save-legacy','版本迁移与旧档保护','保留地图/规则/装备实例快照','读取时验证并对已定义字段迁移生成备份；旧地图不补怪、不重排，新局新增池不追补旧局；旧副属性、旧槽位和原层数按各自兼容路径处理。','ISerializationCallbackReceiver；JsonUtility；C# version 字段与 System.IO 备份。','PrototypeData','public sealed class SessionData')]),
s('render','Unity表现与开发支撑','把逻辑数据呈现为可操作的原型','#697582','SpriteRenderer · Camera · Editor工具',[
 f('render-sprite','像素对象与墙体拼接','16 PPU；Y排序；迷雾遮罩','BuildWorld 从 MapData 创建 GameObject、SpriteRenderer、BoxCollider2D 和 TextMesh。PixelWallTiles 按邻接地形与已知轮廓选择/生成图块，破墙后重建显示。','GameObject / Transform / AddComponent；SpriteRenderer.sortingOrder；Texture2D.GetPixels / Sprite.Create。','PrototypeWorld','partial void BuildWorld'),
 f('render-camera','正交跟随与像素对齐','固定朝向；整数像素缩放','ConfigurePixelCamera 固定正交及屏幕比例；LateUpdate 内跟随显示位置，用 SmoothDamp 平滑相机并对齐像素；人物按方向换Sprite / flipX。','Camera.orthographic；Screen.height；LateUpdate；Vector3.SmoothDamp；SpriteRenderer.flipX。','PrototypeWorld','public static void ConfigurePixelCamera'),
 f('render-editor','资源配置、场景与构建工具','已有编辑器工具源码支撑','PrototypeArtSet / PrototypeAttributeSet 定义资源配置；Editor构建器设导入、创建场景和Windows包。图中仅记录这些现有能力，本次未执行导入、改场景或构建。','ScriptableObject / Resources；TextureImporter / AssetDatabase；EditorSceneManager / BuildPipeline。','PrototypeArtSet','public sealed class PrototypeArtSet'),
 f('render-tests','规则与交互验证支撑','EditMode / PlayMode 测试源码','Tests/Editor 覆盖地图、属性、背包、天赋、设置、交易与存档；Tests/PlayMode 覆盖真实输入、拖拽、轮盘、迷雾和流程。此图依据源码与文档核查，本次未重跑Unity测试。','Unity Test Framework；NUnit [Test]；[UnityTest] / IEnumerator；Input System 测试输入。','PrototypeGame','void Update()')])
]

# Add exact implementation references for editor and test capabilities.
systems[-1]['features'][2]['sources'] += [source('PrototypeSceneBuilder','[MenuItem(', 'Assets/MagicCavePrototype/Editor/'), source('PrototypeSceneBuilder','BuildPipeline.BuildPlayer', 'Assets/MagicCavePrototype/Editor/')]
systems[-1]['features'][3]['sources'] = [source('PrototypeVerification','TestRunnerApi', 'Assets/MagicCavePrototype/Editor/'),source('PrototypePlayTests','[UnityTest]', 'Assets/MagicCavePrototype/Tests/PlayMode/')]

unity_short = [
 ['MonoBehaviour / Update','Debug.Log / JsonUtility','Update / Time.deltaTime','Input System / Application'],
 ['IMGUI / Input System','IMGUI / Texture2D','IMGUI / JsonUtility','IMGUI / JsonUtility'],
 ['Vector2Int + C#生成','Serializable + C#随机流','Vector2Int + C# BFS','Vector2Int + C#验收'],
 ['InputSystem / MoveTowards','Camera / Mouse / C# BFS','Mouse / IMGUI / Time','BoxCollider2D / 自定义格检'],
 ['C#格子视线（无2D灯光）','SpriteRenderer / SetActive','Input System + C#开关','Update + C#走格事件'],
 ['SpriteRenderer + C#掉落','IMGUI / Mouse / Rect','IMGUI / ScriptableObject','Input System / IMGUI'],
 ['ScriptableObject / Resources','Serializable + C# decimal','Serializable + C#生命周期','C#统一数值 / IMGUI显示'],
 ['Update / Time.deltaTime','IMGUI / Input System','SpriteRenderer / IMGUI','IMGUI + C#随机流'],
 ['Serializable / JsonUtility','Vector2Int + C#随机流','IMGUI / Input System','JsonUtility + C#奖励'],
 ['IMGUI / onTextInput','Serializable / IMGUI','JsonUtility + C#校验','JsonUtility 深拷贝'],
 ['persistentDataPath / JSON','Serializable / JsonUtility','JsonUtility + C#账本','序列化回调 / JsonUtility'],
 ['SpriteRenderer / Texture2D','Camera / SmoothDamp','AssetDatabase / BuildPipeline','Unity Test Framework / NUnit']
]
for sys, tags in zip(systems, unity_short):
    for ft, tag in zip(sys['features'], tags):
        ft['unityShort'] = tag

relations = [
 ('town','flow','天赋出发快照','点亮的天赋在开始新局时写入角色与战斗开场效果，已出发局保持原快照。'),
 ('settings','flow','新局规则快照','先生成和应用配置并验证，成功后才替换旧存档。'),
 ('flow','map','生成整局地图','种子与地貌模式决定4张探索图，第5张为固定首领图。'),
 ('map','nav','地形和障碍约束','导航使用实际地形与敌人、商人、永久容器占格。'),
 ('nav','sight','成功移动与破墙','成功走格扣燃烧耐久并揭示，破墙后重新判断视线。'),
 ('sight','nav','揭示范围限制寻路','点击寻路只能通过已揭示地面，大小地图共用揭示状态。'),
 ('map','items','容器与入口风险','位置、容器类别和守卫入口由地图生成；开箱和拾取不校验守卫存活。'),
 ('items','stats','换装与消耗增益','装备和精华改变属性或防御，强攻药剂按战斗数持续。'),
 ('stats','battle','统一卡牌数值','实际ATK/主属性与牌倍率决定伤害和格挡；预览与结算共用公式。'),
 ('nav','battle','相邻敌人主动交互','到达相邻交互位后才切入单敌战斗。'),
 ('battle','nav','胜利后恢复通行','敌人defeated后不再阻挡，返回原探索位置。'),
 ('battle','economy','胜利发放牙齿','普通3、首领10为当前试配；每个敌人只发一次。'),
 ('economy','items','购买消耗品入包','先检查牙齿与容量，成功后入包并尝试填快捷栏空位。'),
 ('items','sight','火把库存与开关','快捷格操作同一根有耐久的火把，影响视野形状并保留状态。'),
 ('battle','save','失败或主动离局结算','战败直接进入失败结算；首领胜利后须再经返回门正式离局。'),
 ('save','town','物品 / 金币 / 声望入账','正式结算入仓与发局外奖励，run.id账本避免重复。'),
 ('town','stats','天赋影响下一局属性','三属性/生命写入本局，战斗机制节点提供开场或战后效果。'),
 ('settings','map','怪物与分布开关','默认生成后按设置移除相应守卫，或配置目标自由怪数量。'),
 ('settings','battle','卡牌与怪物参数','费用、倍率、抽牌、意图数值和奖励开关读本局快照。'),
 ('settings','items','掉落池与物品开关','有效物品按池权重生成，停用池允许空箱。'),
 ('save','flow','继续快照 / 恢复待结算','读取检查点继续本局，或继续未完成的结算。'),
 ('save','map','保存实际地图结果','地形、门、对象、揭示、版本及随机进度随快照保存。'),
 ('save','items','保存物品实例与绑定','独立ID、穿戴槽、背包、容器状态及快捷格一起保存。'),
 ('save','sight','保存揭示与火把状态','恢复剩余耐久与开关；旧清零火把不追补。'),
 ('render','map','绘制数据地图','地图数据驱动精灵、碰撞组件、标签与墙体连接。'),
 ('render','flow','统一界面与更新入口','IMGUI绘制面板，Input System处理交互，Update推进各状态。')
]

data = dict(title='魔洞游戏 · 现有功能思维导图', date='2026-11-03', systems=systems,
 relations=[dict(source=a,target=b,label=c,detail=d) for a,b,c,d in relations],
 scope='当前工作区源码与最新阶段文档；默认设置的新局行为。标注为规则逻辑的功能由C#实现，Unity API负责生命周期、输入、显示、资源与序列化。',
 boundaries=[
 '本图是现有实现说明，不新增玩法规则；默认数值、权重、初始点数等保留原型试配性质。',
 '采用最新规则：4探索图+第5首领、8装备槽、无副属性、点亮即激活、9/21格视野、火把可反复开关。旧5层/8层局保留原快照。',
 '运行时地图由自建网格数据、GameObject和SpriteRenderer实现；通行、寻路、视线是自定义C#，未接入Tilemap、NavMesh或实时2D灯光。',
 '界面为OnGUI/IMGUI绘制，点击与拖拽由Input System自管；未以Canvas/uGUI、UI Toolkit或EventSystem作为这套原型交互主体。',
 'BoxCollider2D确有使用；核心走格碰撞检查是MapData.CanTraverse/CanStand，不应描述为Rigidbody2D物理移动。',
 '本次只读源码和文档并生成导图，没有操作Unity编辑器、改资源或运行Unity测试，也未核实当前资产配置与运行包是否更新。',
 '已知源码边界：全满仓且无可出售/扩容手段时，失败结算仍可能无恢复出口；仓库同名装备属性展示仍取首件。详见原型对照与修正清单R01/R03。',
 '当前未实现的完整版范围：多公会/多职业、速度与独立出牌次数、战斗消耗品牌、元素/神圣/黑暗全规则、任务/制造/物品解锁、理智与完整逃跑流程。'
 ])

def esc(x): return html.escape(str(x), quote=True)
W,H=2500,2200
svg=[]
def add(x): svg.append(x)
def text(x,y,t,size=22,color='#26354d',weight=400):
    add(f'<text x="{x}" y="{y}" font-size="{size}" fill="{color}" font-weight="{weight}">{esc(t)}</text>')
def rect(x,y,w,h,fill,stroke='none',r=14):
    add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}" stroke="{stroke}"/>')

add(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" role="img" aria-label="{esc(data["title"])}" style="font-family:Microsoft YaHei,Segoe UI,sans-serif">')
add('<defs><pattern id="dots" width="24" height="24" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="1" fill="#d9e1eb"/></pattern></defs>')
rect(0,0,W,H,'#f4f7fc',r=0); rect(0,0,W,H,'url(#dots)',r=0)
text(60,62,'MAGIC CAVE / SYSTEM ATLAS',18,'#687b98',700)
text(60,120,'现有功能 · 实现方式 · Unity技术 · 系统联系',42,'#1e2f4b',700)
text(60,159,'2026.11.03  ·  当前工作区源码核查  ·  12个系统 / 48个功能  ·  默认设置的新局规则',21,'#64748b')
rect(60,184,2380,50,'#e5ebf8')
text(85,217,'城镇成长   →   出发快照   →   洞窟探索   ⇄   卡牌战斗 / 升级构筑   →   正式结算   →   城镇成长',23,'#305288',600)

positions={}
for i,sys in enumerate(systems):
    side=0 if i<6 else 1
    row=i%6; x=60 if side==0 else 1690; y=266+row*279; w=750
    positions[sys['id']]=dict(x=x,y=y,w=w,h=255)
    targetx=x+w if side==0 else x; targety=y+32
    startx=1085 if side==0 else 1415
    add(f'<path class="tree-link" data-for="{sys["id"]}" d="M {startx} 1070 C {(startx+targetx)/2} 1070 {(startx+targetx)/2} {targety} {targetx} {targety}" fill="none" stroke="{sys["color"]}" stroke-opacity=".35" stroke-width="2.5"/>')

for i,sys in enumerate(systems):
    p=positions[sys['id']]; x,y,w=p['x'],p['y'],p['w']
    add(f'<g class="system" data-system="{sys["id"]}">')
    rect(x,y,w,255,'#ffffff','#d9e3f0')
    add(f'<g class="node" data-id="{sys["id"]}" tabindex="0" role="button" aria-label="{esc(sys["name"])}">')
    rect(x,y,w,64,sys['color'])
    text(x+20,y+41,f'{i+1:02d}  {sys["name"]}',28,'white',700)
    add('</g>')
    text(x+18,y+88,'Unity · '+sys['unity'],17,'#60748e')
    for j,feature in enumerate(sys['features']):
        fx=x+16+(j%2)*365; fy=y+102+(j//2)*72
        add(f'<g class="node feature" data-id="{feature["id"]}" tabindex="0" role="button" aria-label="{esc(feature["name"])}">')
        rect(fx,fy,353,62,'#f4f7fc','#e1e8f3',9)
        text(fx+12,fy+25,feature['name'],22,'#233a5c',600)
        text(fx+12,fy+49,feature['unityShort'],16,'#6c7b91')
        add('</g>')
    add('</g>')

# Central trunk: explicit relationships remain readable even in the static export.
rect(1045,884,410,330,'#172c50',r=24)
text(1090,939,'魔洞游戏',44,'#ffffff',700)
text(1090,978,'当前可玩原型',24,'#b8ccec')
text(1090,1025,'城镇  /  探索  /  战斗',22,'#ffffff',600)
text(1090,1075,'功能 → 实现 → Unity支撑',21,'#b8ccec')
text(1090,1113,'状态统一存入本局 RunData',20,'#b8ccec')
text(1090,1169,'点击节点查看源码和关系',20,'#7dd3c7')

chains=[('01','构筑与数值','天赋 / 换装 → 属性 → 卡牌数值'),('02','探索补给','战斗 → 牙齿 → 商人 → 消耗品'),('03','信息与路线','移动 / 火把 → 揭示 → 寻路'),('04','局外成长','正式结算 → 仓库 / 金币 / 声望'),('05','规则与恢复','天赋 / 设置 → 新局；存档 → 继续')]
for j,(num,title,caption) in enumerate(chains):
    y=300+j*102 if j<3 else 1330+(j-3)*102
    rect(886,y,728,82,'#edf2fa','#d7e2f1',12)
    text(906,y+30,num+'  '+title,21,'#354f77',700)
    text(906,y+62,caption,23,'#4d6487')
rect(886,1590,728,275,'#edf2fa','#d7e2f1',12)
text(908,1625,'Unity技术标注',24,'#354f77',700)
for j,line in enumerate(['输入  Input System   ·   界面  IMGUI','显示  SpriteRenderer / Camera / Texture2D','配置  ScriptableObject / Resources','存储  JsonUtility + System.IO','逻辑  自建网格 / BFS / 射线 / 数值公式','开发  Editor API / Unity Test Framework']):
    text(908,1666+j*31,line,19,'#526884')

rect(60,1970,2380,177,'#ffffff','#d9e3f0')
text(84,2008,'阅读方式',23,'#243b60',700)
text(220,2008,'彩色分支 = 系统归属；浅色子节点 = 具体功能；中部链路 = 数据和玩法联系。',22,'#536882')
text(84,2050,'当前规则',21,'#243b60',700)
text(220,2050,'4张探索图 + 第5张首领；八槽装备；新局无副属性；天赋点亮即激活；视野9 / 21格；火把可反复开关。',21,'#536882')
text(84,2090,'核查边界',21,'#243b60',700)
text(220,2090,'基于源码与阶段文档；本次未操作Unity编辑器、重跑游戏测试或构建。旧存档兼容和试配数值按各自范围保留。',21,'#536882')
text(84,2126,'交互版提供：缩放 / 搜索 / 节点实现详情 / 逐项Unity API / 源码行号 / 系统关系图 / 完整边界说明。',19,'#71829b')
add('</svg>')
svg_text='\n'.join(svg)
(HERE/'现有功能思维导图.svg').write_text(svg_text,encoding='utf-8')
data['positions']=positions
(HERE/'mindmap-data.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')

from unity_guide import build_guides
data['unityGuides'] = build_guides(source)
(HERE/'mindmap-data.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
template=(HERE/'mindmap-template.html').read_text(encoding='utf-8')
(HERE/'现有功能思维导图.html').write_text(template.replace('<!--MAP-->',svg_text).replace('/*DATA*/',json.dumps(data,ensure_ascii=False).replace('</','<\\/')),encoding='utf-8')

# Editable outline + evidence, using one source of truth for all formats.
md=['# 魔洞游戏现有功能思维导图','',f'核查日期：{data["date"]}。{data["scope"]}','',
'[打开交互导图](现有功能思维导图.html) · [查看高清图片](现有功能思维导图.png) · [查看矢量图](现有功能思维导图.svg) · [可导入FreeMind/XMind的源文件](现有功能思维导图.mm)','',
'交互版支持滚轮缩放、拖动画布、搜索、点击节点查看实现和Unity技术、系统关系图。HTML不需要联网或安装依赖。`.mm`为FreeMind格式，使用支持该格式导入的软件继续编辑。','',
'## 主循环和系统联系','','```mermaid','flowchart LR','  Town[城镇天赋 / 公会 / 仓库] -->|规则与天赋快照| Run[开始新局]','  Settings[功能与数值设置] --> Run','  Run --> Map[地貌 / 地图 / 容器 / 怪物]','  Map --> Nav[输入 / 移动 / 寻路 / 交互]','  Nav --> Sight[火把计步 / 视野 / 揭示]','  Sight -->|已揭示路线| Nav','  Nav --> Battle[单敌卡牌战斗]','  Battle --> Growth[经验 / 升级选牌]','  Growth -->|构筑卡组| Battle','  Battle --> Teeth[牙齿 / 集齿者]','  Teeth --> Items[消耗品 / 背包 / 装备]','  Items --> Stats[三属性 / ATK / 防御]','  Town -->|下一局天赋| Stats','  Stats -->|牌倍率 / 格挡 / 伤害公式| Battle','  Items -->|火把开关| Sight','  Battle --> Settlement[正式离局或失败结算]','  Settlement -->|物品 / 金币 / 声望| Town','  Nav -->|返回门暂存| Save[本局快照 / 独立随机流]','  Save -->|继续原局| Nav','```','',
'## 功能、实现与Unity标注','',
'这里将Unity支撑和自定义规则分开标注：BFS、随机流、镇守入口验证、视线算法、伤害公式、经济规则属于项目C#实现。Unity提供输入、显示、生命周期、资源配置、序列化与编辑器能力。','']
for i,sys in enumerate(systems):
    md += [f'### {i+1:02d} {sys["name"]}','',sys['role']+'。','']
    for ft in sys['features']:
        md += [f'#### {ft["name"]}','',f'- 功能：{ft["brief"]}。',f'- 实现：{ft["how"]}',f'- Unity功能 / API：{ft["unity"]}', '- 源码：'+'；'.join(f'[{Path(q["path"]).name}:{q["line"]}](../../{q["path"]}#L{q["line"]}) — `{q["symbol"]}`' for q in ft['sources']), '']
md += ['## 跨系统关系表','','| 来源系统 | 目标系统 | 作用 | 说明 |','|---|---|---|---|']
names={q['id']:q['name'] for q in systems}
for r in data['relations']: md += [f'| {names[r["source"]]} | {names[r["target"]]} | {r["label"]} | {r["detail"]} |']
md += ['','## 适用范围、未实现项与核查限制',''] + ['- '+q for q in data['boundaries']]
md += ['','## 文档依据','']
docs=['最小可玩模型范围_v0.1.md','背包装备与三属性星图_2026-11-02.md','鼠标轮盘与小地图试验_2026-11-03.md','三格消耗品快捷栏_2026-11-03.md','主界面设置面板_2026-11-03.md','箱袋随机掉落与美术接入.md','牙齿与集齿者_2026-11-02.md','角色属性与装备系统完整原型设计_v1.0.md','世界地图初版说明.md','原型对照与修正清单.md']
md += [f'- [{q}](../{q})（采用适用范围内的最新修订；历史数值不自动视为当前规则）' for q in docs]
md += ['','## 更新方法','','编辑 `build_mindmap.py` 中的系统、功能和关系数据，运行 `python build_mindmap.py` 重新生成HTML、SVG、JSON、Markdown和FreeMind源文件。PNG由浏览器渲染SVG导出。仅生成文档，不连接Unity或写入Unity资源。','']
(HERE/'现有功能思维导图.md').write_text('\n'.join(md),encoding='utf-8')

mm=ET.Element('map',version='1.0.1')
root=ET.SubElement(mm,'node',TEXT=data['title'],ID='root')
for i,sys in enumerate(systems):
    branch=ET.SubElement(root,'node',TEXT=sys['name'],ID=sys['id'],COLOR=sys['color'],POSITION='left' if i<6 else 'right')
    ET.SubElement(branch,'node',TEXT='用途：'+sys['role'])
    ET.SubElement(branch,'node',TEXT='Unity：'+sys['unity'])
    for ft in sys['features']:
        leaf=ET.SubElement(branch,'node',TEXT=ft['name'],ID=ft['id'],FOLDED='true')
        ET.SubElement(leaf,'node',TEXT='功能：'+ft['brief'])
        ET.SubElement(leaf,'node',TEXT='实现：'+ft['how'])
        ET.SubElement(leaf,'node',TEXT='Unity功能：'+ft['unity'])
        for q in ft['sources']: ET.SubElement(leaf,'node',TEXT=f'{q["path"]}:{q["line"]} — {q["symbol"]}')
    for rel in data['relations']:
        if rel['source']==sys['id']:
            link=ET.SubElement(branch,'node',TEXT='关联 → '+names[rel['target']]+'：'+rel['label'],FOLDED='true')
            ET.SubElement(link,'node',TEXT=rel['detail'])
limits=ET.SubElement(root,'node',TEXT='范围与核查限制',FOLDED='true',POSITION='right')
for b in data['boundaries']: ET.SubElement(limits,'node',TEXT=b)
ET.indent(mm)
ET.ElementTree(mm).write(HERE/'现有功能思维导图.mm',encoding='utf-8',xml_declaration=True)
print(json.dumps(dict(systems=len(systems),features=sum(len(s['features']) for s in systems),relations=len(relations),output=str(HERE)),ensure_ascii=False))
