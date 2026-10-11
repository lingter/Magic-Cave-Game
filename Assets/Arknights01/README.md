# 明日方舟 0-1「坍塌」场景复刻

打开 `Assets/Scenes/Arknights_0_1.unity`，Game 视图建议使用 16:9。

地图由可编辑的三维网格、材质、地块碰撞体构成。原布局为 9 列 × 6 行，每格 2 米；原始坐标行 0 在南侧、列 0 在西侧。16 个远程高台、12 个近战地块、24 个禁入地块，另有 1 个入口与 1 个目标点。

地图根对象包含 `ArknightsMapLayout`，保存地块类型、部署查询和敌人路线采样点。选中根对象可查看路线 Gizmos。路线含原始数据中的四个 MOVE 检查点，额外采样点沿道路绕开高台。此场景提供静态地图，不包含战斗、刷怪或干员系统。

`Tools > Arknights > Build 0-1 Collapse` 可重建地图并保存场景。该操作替换同名地图根对象，调整当前相机、主光源和全局后处理。道路与高台有独立 Prefab，材质与纹理均保存在此目录。

地块来源：https://github.com/Kengxxiao/ArknightsGameData/blob/master/zh_CN/gamedata/levels/obt/main/level_main_00-01.json

视觉参考：https://prts.wiki/w/0-1_坍塌 和 https://torappu.prts.wiki/assets/map_preview/main_00-01.png

几何和材质为程序构建的近似复刻，未使用游戏原版模型、材质或贴图；Reference 目录中的截图只用于对照。
