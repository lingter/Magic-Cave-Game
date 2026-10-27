using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.Prototype
{
    public enum TownPage { None, Guild, Mentor, Chairman, Expedition }

    public sealed partial class PrototypeGame
    {
        public TownPage TownPage { get; private set; }
        public bool InGuild { get; private set; }
        public Vector2 TownPosition { get; private set; } = new Vector2(12, 11);
        public bool TownWalking => townRoute.Count > 0;
        readonly List<Vector2Int> townRoute = new List<Vector2Int>();
        readonly Dictionary<string, Texture2D> townTextures = new Dictionary<string, Texture2D>();
        int townDestination = -1, selectedTalent = 1;
        bool townEscapeHandled;
        Vector2 townFacing = Vector2.down;
        Vector2 townOutsidePosition = new Vector2(12, 11);
        const int TownWidth = 26, TownHeight = 15, TownTile = 32;
        static readonly Rect TownMapRect = new Rect(24, 137, TownWidth * TownTile, TownHeight * TownTile);
        static readonly Vector2Int[] TownStops = { new Vector2Int(5, 5), new Vector2Int(5, 5), new Vector2Int(5, 5), new Vector2Int(5, 5), new Vector2Int(13, 5) };
        static readonly Vector2Int[] GuildStops = { new Vector2Int(12, 11), new Vector2Int(17, 5), new Vector2Int(7, 5), new Vector2Int(12, 9), new Vector2Int(12, 13) };
        Vector2Int[] ActiveTownStops => InGuild ? GuildStops : TownStops;
        static readonly string[] TownNames = { "战士公会", "导师", "会长", "仓库", "洞窟传送门" };
        static readonly RectInt[] TownSolids =
        {
            new RectInt(3, 1, 4, 4), new RectInt(11, 1, 4, 4)
        };
        static readonly RectInt[] GuildSolids = { new RectInt(17, 4, 1, 1), new RectInt(7, 4, 1, 1), new RectInt(11, 8, 2, 1) };
        static readonly Vector2[] TalentPositions =
        {
            new Vector2(408, 209), new Vector2(220, 316), new Vector2(548, 316),
            new Vector2(688, 419), new Vector2(220, 509), new Vector2(502, 509), new Vector2(688, 564)
        };

        public static bool TownWalkable(int x, int y, bool guild = false)
        {
            if (x < 1 || y < 1 || x >= TownWidth - 2 || y >= TownHeight - 1) return false;
            return !(guild ? GuildSolids : TownSolids).Any(rect => rect.Contains(new Vector2Int(x, y)));
        }
        bool TownFits(Vector2 position)
        {
            const float radius = 0.35f;
            return TownWalkable(Mathf.FloorToInt(position.x - radius + .5f), Mathf.FloorToInt(position.y - radius + .5f), InGuild) &&
                TownWalkable(Mathf.FloorToInt(position.x + radius + .5f), Mathf.FloorToInt(position.y - radius + .5f), InGuild) &&
                TownWalkable(Mathf.FloorToInt(position.x - radius + .5f), Mathf.FloorToInt(position.y + radius + .5f), InGuild) &&
                TownWalkable(Mathf.FloorToInt(position.x + radius + .5f), Mathf.FloorToInt(position.y + radius + .5f), InGuild);
        }
        public void CloseTownPage()
        {
            TownPage = TownPage.None; townRoute.Clear(); townDestination = -1; seedFocused = false;
        }
        public void VisitTown(int destination)
        {
            if (ScreenMode != GameScreen.Home || Panel != GamePanel.None || destination < 0 || destination >= TownStops.Length) return;
            TownPage = TownPage.None;
            if (InGuild && destination == 0) { TownPage = TownPage.Guild; return; }
            NavigateTown(ActiveTownStops[destination], destination);
        }
        void NavigateTown(Vector2Int target, int destination = -1)
        {
            townRoute.Clear(); townDestination = -1;
            if (!TownWalkable(target.x, target.y, InGuild)) { Notify("这里无法通行"); return; }
            var start = new Vector2Int(Mathf.RoundToInt(TownPosition.x), Mathf.RoundToInt(TownPosition.y));
            var queue = new Queue<Vector2Int>(); var previous = new Dictionary<Vector2Int, Vector2Int>();
            queue.Enqueue(start); previous.Add(start, start);
            while (queue.Count > 0 && !previous.ContainsKey(target))
            {
                var current = queue.Dequeue();
                foreach (var direction in MapGenerator.Directions)
                {
                    var next = current + direction;
                    if (!TownWalkable(next.x, next.y, InGuild) || previous.ContainsKey(next)) continue;
                    previous.Add(next, current); queue.Enqueue(next);
                }
            }
            if (!previous.ContainsKey(target)) { Notify("无法到达"); return; }
            var path = new List<Vector2Int>(); var cell = target;
            while (cell != start) { path.Add(cell); cell = previous[cell]; }
            path.Reverse(); townRoute.Add(start); townRoute.AddRange(path); townDestination = destination;
        }
        void OpenTownDestination(int destination)
        {
            townRoute.Clear(); townDestination = -1;
            if (!InGuild && destination != 4)
            {
                townOutsidePosition = TownPosition; InGuild = true; TownPosition = new Vector2(12, 11);
                TownPage = TownPage.None;
                if (destination != 0) VisitTown(destination);
                return;
            }
            if (InGuild && destination == 4) { ExitGuild(); return; }
            if (destination == 3) { ScreenMode = GameScreen.Warehouse; return; }
            TownPage = destination == 0 ? TownPage.Guild : destination == 1 ? TownPage.Mentor : destination == 2 ? TownPage.Chairman : TownPage.Expedition;
        }
        void ExitGuild()
        {
            InGuild = false; TownPosition = townOutsidePosition; CloseTownPage();
        }
        void ResetTownLocation()
        {
            if (InGuild) ExitGuild(); else CloseTownPage();
        }
        int NearbyTown()
        {
            int closest = -1; float distance = 1.35f;
            for (int i = 0; i < TownStops.Length; i++)
            {
                if ((InGuild && i == 0) || (!InGuild && i != 0 && i != 4)) continue;
                float value = Vector2.Distance(TownPosition, ActiveTownStops[i]);
                if (value < distance) { distance = value; closest = i; }
            }
            return closest;
        }
        void UpdateTown()
        {
            townEscapeHandled = false;
            if (ScreenMode == GameScreen.Warehouse && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { LeaveWarehouse(); townEscapeHandled = true; return; }
            if (ScreenMode != GameScreen.Home || Panel != GamePanel.None) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { CloseTownPage(); return; }
            if (TownPage != TownPage.None) return;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
            {
                int nearby = NearbyTown(); if (nearby >= 0) OpenTownDestination(nearby);
                return;
            }
            Vector2 input = Vector2.zero;
            if (keyboard != null)
            {
                input.x = ((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1 : 0) - ((keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1 : 0);
                input.y = ((keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) ? 1 : 0) - ((keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) ? 1 : 0);
            }
            if (input.sqrMagnitude > 0)
            {
                townRoute.Clear(); townDestination = -1;
                var delta = input.normalized * (4 * Mathf.Min(Time.deltaTime, .1f));
                var next = TownPosition + new Vector2(delta.x, 0); if (TownFits(next)) TownPosition = next;
                next = TownPosition + new Vector2(0, delta.y); if (TownFits(next)) TownPosition = next;
                townFacing = new Vector2(input.x, -input.y);
            }
            else if (townRoute.Count > 0)
            {
                var next = Vector2.MoveTowards(TownPosition, townRoute[0], 4 * Mathf.Min(Time.deltaTime, .1f));
                var delta = next - TownPosition;
                if (delta.sqrMagnitude > 0) townFacing = new Vector2(delta.x, -delta.y);
                TownPosition = next;
                if (Vector2.Distance(next, townRoute[0]) < .001f)
                {
                    townRoute.RemoveAt(0);
                    if (townRoute.Count == 0 && townDestination >= 0) OpenTownDestination(townDestination);
                }
            }
        }
        Texture2D TownTexture(string name)
        {
            if (!townTextures.TryGetValue(name, out var texture))
            {
                texture = Resources.Load<Texture2D>("WorldMapArt/" + name); townTextures[name] = texture;
            }
            return texture;
        }
        void TownImage(Rect rect, string name, Color? tint = null)
        {
            var texture = TownTexture(name); if (texture == null) return;
            var old = GUI.color; GUI.color = tint ?? Color.white; GUI.DrawTexture(rect, texture); GUI.color = old;
        }
        Rect TownCell(float x, float y, float width = 1, float height = 1) =>
            new Rect(TownMapRect.x + x * TownTile, TownMapRect.y + y * TownTile, width * TownTile, height * TownTile);
        void TownCaption(float x, float y, string caption, bool gold = false)
        {
            var cell = TownCell(x, y); var rect = new Rect(cell.center.x - 75, cell.y, 150, 26);
            Fill(rect, "town caption", new Color(.04f, .065f, .09f, .94f));
            Label(rect, caption, buttonStyle, gold ? Gold : Ink);
        }
        void DrawTownHeader(string subtitle)
        {
            Label(new Rect(26, 16, 650, 44), InGuild ? "战士公会  /  骑士营地" : "魔洞边镇  /  世界地图", headingStyle, Gold);
            Label(new Rect(27, 66, 790, 28), subtitle, smallStyle, Cyan);
            if (Store == null) return;
            var town = Store.Warehouse.town;
            Box(new Rect(804, 17, 450, 78));
            Label(new Rect(825, 23, 408, 32), "金币  " + town.gold + "       声望  " + town.reputation, textStyle, Gold);
            Label(new Rect(825, 58, 408, 28), "骑士  ·  战士公会 Lv." + town.guildLevel + "    仓库 " + TownRules.Used(Store.Warehouse) + "/" + TownRules.Capacity(Store.Warehouse), smallStyle);
        }
        void DrawTownHome()
        {
            DrawTownHeader("WASD / 方向键移动    左键寻路 / 拜访    E 交互    Esc 关闭面板");
            if (Store == null) { Label(new Rect(60, 160, 1100, 100), "城镇存档未能载入，请查看下方提示。"); return; }
            if (TownPage == TownPage.Mentor) DrawTalentTree();
            else DrawTownMap();
            Box(new Rect(880, 119, 374, 510));
            switch (TownPage)
            {
                case TownPage.Guild: DrawGuild(); break;
                case TownPage.Mentor: DrawTalentDetails(); break;
                case TownPage.Chairman: DrawChairman(); break;
                case TownPage.Expedition: DrawExpedition(); break;
                default: DrawTownGuide(); break;
            }
            if (TownPage != TownPage.None) Button(new Rect(899, 578, 336, 35), InGuild ? "返回公会 [Esc]" : "返回城镇 [Esc]", CloseTownPage);
            Label(new Rect(26, 653, 1190, 36), "局外成长自动保存  ·  存档回城保留本局，结束探索才结算物品、金币与声望  ·  当前为初版数值", smallStyle);
        }
        void DrawTownMap()
        {
            Box(new Rect(22, 119, 836, 510));
            // Source tiles are assembled before scaling, so adjacent cells never expose a seam.
            TownImage(TownMapRect, "Town_GrassSurface");
            foreach (var step in townRoute)
            {
                var cell = TownCell(step.x, step.y);
                Fill(new Rect(cell.center.x - 2, cell.center.y - 2, 4, 4), "town route", new Color(.95f, .77f, .35f));
            }
            if (InGuild)
            {
                TownImage(TownCell(7, 4), "Town_Chairman");
                TownImage(TownCell(17, 4), "Town_Mentor");
                TownImage(TownCell(11, 8, 2, 1), "Town_Warehouse");
                TownCaption(7, 3, "会长"); TownCaption(17, 3, "导师"); TownCaption(11.5f, 7, "仓库");
                var exit = TownCell(11, 13, 3, 1); Fill(exit, "town exit", new Color(.08f, .12f, .16f)); Label(exit, "返回城镇", buttonStyle);
            }
            else
            {
                TownImage(TownCell(3, 1, 4, 4), "Town_Building_Tent");
                TownImage(TownCell(11, 1, 4, 4), "Town_Building_TheCave");
                TownCaption(5, 0, "战士公会", true); TownCaption(13, 0, "洞窟传送门", true);
            }
            if (artSet != null)
            {
                bool horizontal = Mathf.Abs(townFacing.x) > Mathf.Abs(townFacing.y);
                var sprite = horizontal ? artSet.playerSide : townFacing.y > 0 ? artSet.playerUp : artSet.playerDown;
                if (sprite != null)
                {
                    var old = GUI.color; GUI.color = Color.white;
                    Rect uv = new Rect(sprite.rect.x / sprite.texture.width, sprite.rect.y / sprite.texture.height, sprite.rect.width / sprite.texture.width, sprite.rect.height / sprite.texture.height);
                    if (horizontal && townFacing.x < 0) { uv.x += uv.width; uv.width = -uv.width; }
                    GUI.DrawTextureWithTexCoords(TownCell(TownPosition.x, TownPosition.y), sprite.texture, uv); GUI.color = old;
                }
            }
            var position = TownCell(TownPosition.x, TownPosition.y);
            Fill(new Rect(position.center.x - 12, position.yMax + 1, 24, 3), "town player mark", Gold);
            int nearby = NearbyTown();
            if (nearby >= 0) Label(new Rect(30, 601, 810, 25), "[E] " + NearbyTownAction(nearby), buttonStyle, Gold);
            if (TownPage != TownPage.None || !GUI.enabled || !Click(TownMapRect)) return;
            var point = GuiMouse;
            var target = new Vector2Int(Mathf.FloorToInt((point.x - TownMapRect.x) / TownTile), Mathf.FloorToInt((point.y - TownMapRect.y) / TownTile));
            for (int i = 0; i < TownStops.Length; i++)
            {
                if ((InGuild && i == 0) || (!InGuild && i != 0 && i != 4)) continue;
                bool hit = InGuild ? i == 4 ? new RectInt(11, 13, 3, 1).Contains(target) : GuildSolids[i == 1 ? 0 : i == 2 ? 1 : 2].Contains(target) : TownSolids[i == 0 ? 0 : 1].Contains(target);
                var label = InGuild ? i == 4 ? TownCell(12, 13) : TownCell(ActiveTownStops[i].x, ActiveTownStops[i].y - 2) : TownCell(ActiveTownStops[i].x, 0);
                if (hit || new Rect(label.center.x - 75, label.y, 150, 26).Contains(point))
                { int destination = i; Queue(() => VisitTown(destination)); return; }
            }
            Queue(() => NavigateTown(target));
        }
        void DrawTownGuide()
        {
            Label(new Rect(901, 136, 325, 42), InGuild ? "战士公会 · 骑士" : "出发之前", headingStyle, Gold);
            int nearby = NearbyTown();
            Label(new Rect(902, 184, 323, 75), nearby >= 0 ? NearbyTownAction(nearby) : "点击地点自动前往。\n走近后可查看功能并交互。", smallStyle);
            for (int i = 0; i < TownStops.Length; i++)
            {
                if (!InGuild && i != 0 && i != 4) continue;
                int destination = i;
                string caption = InGuild ? (i == 0 ? "骑士职业" : i == 4 ? "返回城镇" : TownNames[i]) : TownNames[i];
                Button(new Rect(901, 272 + (InGuild ? i : i == 0 ? 0 : 1) * 51, 330, 42), caption, () => VisitTown(destination), true, i == 4);
            }
            if (nearby >= 0) Button(new Rect(901, 228, 330, 34), "交互 [E]", () => OpenTownDestination(nearby), true, true);
            Button(new Rect(901, 555, 330, 42), "退出游戏", () => { leaveApplication = true; Panel = GamePanel.Leave; });
        }
        string NearbyTownAction(int destination)
        {
            if (!InGuild) return destination == 0 ? "进入战士公会" : "进入洞窟 · 新局 / 继续存档";
            return destination == 1 ? "导师：学习与重置骑士天赋" : destination == 2 ? "会长：查看公会等级与升级要求" : destination == 3 ? "仓库：存储、出售与扩容" : "离开公会，返回城镇";
        }
        void DrawGuild()
        {
            Label(new Rect(901, 139, 330, 45), "战士公会", headingStyle, Gold);
            Label(new Rect(901, 201, 330, 38), "当前职业：骑士", headingStyle, Cyan);
            Label(new Rect(901, 252, 330, 120), "攻守兼备，善用剑与盾。\n起始卡组：5 打击 / 4 防御 / 1 整备。\n天赋强化基础属性，并解锁战斗机制。", textStyle);
            Button(new Rect(901, 402, 330, 44), "拜访导师 · 天赋树", () => { CloseTownPage(); VisitTown(1); });
            Button(new Rect(901, 460, 330, 44), "拜访会长 · 公会升级", () => { CloseTownPage(); VisitTown(2); });
            Label(new Rect(901, 520, 330, 40), "本阶段开放战士公会 · 骑士。", smallStyle);
        }
        void DrawChairman()
        {
            var town = Store.Warehouse.town; bool maximum = town.guildLevel >= 3;
            Label(new Rect(901, 139, 330, 44), "会长 · 公会升级", headingStyle, Gold);
            Label(new Rect(901, 198, 330, 44), "战士公会  Lv." + town.guildLevel, headingStyle);
            Label(new Rect(901, 254, 330, 104), "Lv.1  开放基础属性训练\nLv.2  开放破阵先锋、不屈意志\nLv.3  开放盾壁", textStyle);
            Label(new Rect(901, 378, 330, 90), maximum ? "已达到初版最高等级。" : "升至 Lv." + (town.guildLevel + 1) + "\n花费 " + TownRules.UpgradeGold(town) + " 金币\n要求声望 " + TownRules.UpgradeReputation(town) + "（不消耗）", textStyle, Cyan);
            Button(new Rect(901, 490, 330, 48), maximum ? "已满级" : "升级公会", () => { Store.UpgradeGuild(); Notify("公会升级成功，新的天赋已开放"); },
                !maximum && town.gold >= TownRules.UpgradeGold(town) && town.reputation >= TownRules.UpgradeReputation(town), true);
        }
        void DrawExpedition()
        {
            var pending = Store.Session.pending != null;
            Label(new Rect(901, 139, 330, 44), "洞窟传送门", headingStyle, Gold);
            Label(new Rect(901, 192, 330, 59), "5 张地图 · 本局成长独立\n新局应用当前骑士天赋。", smallStyle);
            Button(new Rect(901, 269, 330, 48), "开始新局", RequestNewRun, !pending, true);
            Button(new Rect(901, 332, 330, 48), pending ? "继续未完成结算" : "继续存档", ContinueRun, pending || Store.Session.checkpoint != null);
            Button(new Rect(901, 398, 330, 38), customSeed ? "☑ 自定义种子" : "□ 随机种子", () => { customSeed = !customSeed; seedFocused = customSeed; });
            var input = new Rect(901, 450, 240, 40);
            Fill(input, "town seed input", new Color(.09f, .15f, .22f));
            Label(new Rect(909, 456, 224, 30), customSeed ? customSeedText + (seedFocused && Mathf.FloorToInt(Time.unscaledTime * 2) % 2 == 0 ? "|" : "") : "每局随机 Seed");
            if (Click(input) && customSeed) seedFocused = true;
            Button(new Rect(1152, 450, 78, 40), "清空", () => { customSeedText = ""; seedFocused = true; }, customSeed);
            Label(new Rect(901, 511, 330, 57), "存档继续沿用出发时的天赋，\n回城学习不会改写进行中的本局。", smallStyle);
        }
        void DrawTalentTree()
        {
            Box(new Rect(22, 119, 836, 510));
            Label(new Rect(47, 132, 765, 44), "骑士天赋  /  剑与盾的誓约", headingStyle, Gold);
            var town = Store.Warehouse.town;
            for (int i = 1; i < TalentPositions.Length; i++)
            {
                var from = TalentPositions[TownRules.Talents[i].parent]; var to = TalentPositions[i];
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, from);
                Fill(new Rect(from.x, from.y - 1, Vector2.Distance(from, to), 3), town.talents[i] > 0 ? "talent active edge" : "talent edge", town.talents[i] > 0 ? Gold : new Color(.23f, .33f, .40f));
                GUI.matrix = old;
            }
            for (int i = 0; i < TalentPositions.Length; i++)
            {
                int id = i; var node = TownRules.Talents[i]; var position = TalentPositions[i];
                var rect = new Rect(position.x - 74, position.y - 27, 148, 54);
                if (selectedTalent == i) Fill(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), "talent selected", Cyan);
                Button(rect, node.name + "\n" + town.talents[i] + " / " + node.maximum, () => selectedTalent = id, true, town.talents[i] > 0);
            }
            Label(new Rect(47, 589, 400, 25), "小节点：基础属性    大节点：战斗机制", smallStyle);
        }
        void DrawTalentDetails()
        {
            var town = Store.Warehouse.town; var node = TownRules.Talents[selectedTalent];
            Label(new Rect(901, 139, 330, 44), "导师 · " + node.name, headingStyle, Gold);
            Label(new Rect(901, 204, 330, 100), node.effect);
            Label(new Rect(901, 317, 330, 30), "当前等级 " + town.talents[selectedTalent] + " / " + node.maximum, textStyle, Cyan);
            bool maximum = town.talents[selectedTalent] >= node.maximum;
            string requirement = node.parent < 0 ? "基础职业节点" : "前置：" + TownRules.Talents[node.parent].name + " " + node.parentRank + "级";
            Label(new Rect(901, 361, 330, 88), requirement + "\n公会 Lv." + node.guildLevel + "  /  声望 " + node.reputation + "\n" + (maximum ? "已经点满" : "下一级花费 " + TownRules.TalentGold(town, selectedTalent) + " 金币"), smallStyle);
            string reason = TownRules.TalentLock(town, selectedTalent);
            Button(new Rect(901, 458, 330, 44), reason.Length == 0 ? "学习天赋" : reason, () => { Store.LearnTalent(selectedTalent); Notify("天赋已保存，下次出发时生效"); }, reason.Length == 0, true);
            Button(new Rect(901, 517, 330, 38), "重置天赋 · 全额返还金币", () => { Store.ResetTalents(); Notify("天赋已重置，金币已返还"); }, town.talents.Skip(1).Any(rank => rank > 0));
        }
        void DrawTownWarehouse()
        {
            if (Store == null) return;
            DrawTownHeader("仓库  /  正式结算的带出物品存放于此");
            var warehouse = Store.Warehouse; var town = warehouse.town;
            Label(new Rect(70, 133, 1100, 45), "仓库  " + TownRules.Used(warehouse) + " / " + TownRules.Capacity(warehouse) + " 件", headingStyle, Gold);
            Label(new Rect(70, 187, 1100, 35), "每件占用 1 容量。出售获得金币；捐赠古代遗物获得声望。新局背包为空。", smallStyle);
            if (warehousePage == 0)
            Button(new Rect(947, 180, 260, 32), "装备与新增物品 →", () => warehousePage = 1);
            for (int i = 0; i < 4 && warehousePage == 0; i++)
            {
                int kind = i; float y = 241 + i * 70;
                Box(new Rect(70, y, 1140, 58));
                Label(new Rect(87, y + 12, 290, 33), Rules.ItemNames[i] + "   × " + warehouse.counts[i]);
                Label(new Rect(390, y + 16, 250, 30), "售价 " + TownRules.SellPrices[i] + " 金币 / 件", smallStyle, Cyan);
                Button(new Rect(664, y + 9, 135, 39), "出售 1 件", () => { Store.SellWarehouse((ItemKind)kind, 1); Notify("出售成功"); }, warehouse.counts[i] > 0);
                Button(new Rect(815, y + 9, 135, 39), "出售 10 件", () => { Store.SellWarehouse((ItemKind)kind, 10); Notify("出售成功"); }, warehouse.counts[i] >= 10);
                if (i == (int)ItemKind.Relic) Button(new Rect(967, y + 9, 220, 39), "捐赠 1 件 · 声望 +10", () => { Store.DonateRelic(); Notify("捐赠成功，声望 +10"); }, warehouse.counts[i] > 0);
            }
            if (warehousePage > 0)
            {
                Button(new Rect(883, 181, 145, 31), "上一页", () => warehousePage--);
                Button(new Rect(1045, 181, 162, 31), "下一页", () => warehousePage++, warehousePage < 2);
                int first = 4 + (warehousePage - 1) * 6;
                for (int i = first; i < Math.Min(first + 6, Rules.ItemNames.Length); i++)
                {
                    var kind = (ItemKind)i; float y = 228 + (i - first) * 49;
                    long count = warehouse.counts[i] + warehouse.equipment.Count(e => e.item.kind == kind);
                    Box(new Rect(70, y, 1140, 45));
                    Label(new Rect(87, y + 8, 330, 30), Rules.ItemNames[i] + " ×" + count);
                    var stored = warehouse.equipment.FirstOrDefault(e => e.item.kind == kind);
                    string detail = stored == null ? "首版仅存储，暂不出售或携入新局" : stored.definition.slot == EquipmentSlot.MainHand ?
                        "基础ATK " + stored.definition.baseAttack + "；力/敏/智修正 " + stored.definition.strengthScaling / 100m + "% / " + stored.definition.agilityScaling / 100m + "% / " + stored.definition.wisdomScaling / 100m + "%" :
                        "力/敏/智 +" + stored.definition.strength + "/" + stored.definition.agility + "/" + stored.definition.wisdom + "；生命 +" + stored.definition.health + "；防御 +" + stored.definition.defense;
                    Label(new Rect(420, y + 10, 770, 30), detail, smallStyle, Cyan);
                }
            }
            Button(new Rect(70, 553, 560, 48), town.warehouseUpgrades >= 10 ? "仓库已扩容至上限" : "扩容 +50  /  " + TownRules.ExpansionGold(town) + " 金币", () => { Store.ExpandWarehouse(); Notify("仓库已扩容"); }, town.warehouseUpgrades < 10 && town.gold >= TownRules.ExpansionGold(town), true);
            Button(new Rect(722, 553, 488, 48), Store.Session.pending != null ? "返回待完成结算" : InGuild ? "返回公会" : "返回世界地图", () =>
                LeaveWarehouse());
            Label(new Rect(70, 635, 1140, 35), "金币、声望和仓库操作立即保存。声望是公会与天赋的解锁条件，不会在升级时扣除。", smallStyle);
        }
        void LeaveWarehouse()
        {
            if (Store.Session.pending != null) { Run = Store.Session.pending.run; ScreenMode = GameScreen.Settlement; }
            else { ScreenMode = GameScreen.Home; CloseTownPage(); }
        }
        string CardEffect(CardKind card)
        {
            return Run == null ? Rules.Effects[(int)card] : CardDescription(Run, card, ScreenMode == GameScreen.Battle);
        }
    }
}
