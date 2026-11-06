using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    public sealed class OnlineRoomForm : Form
    {
        private sealed class AddressChoice
        {
            public IPAddress Address;
            public string InterfaceName;
            public override string ToString() { return Address + " · " + InterfaceName; }
        }

        private readonly EngineSettings engineSettings;
        private readonly long visits;
        private readonly StoneSound sound;
        private readonly Label title, subtitle, status, addressHelp, roomInfo, positionInfo, proposalText, engineInfo;
        private readonly Panel lobby, room;
        private readonly WarmCard matchActions, studyActions, proposalCard, analysisCard;
        private readonly ComboBox network, address, mode, size, rules, hostColor, handicapColor, studyTool, analysisChoice;
        private readonly NumericUpDown port, komi, handicap, jumpMove;
        private readonly TextBox hostName, guestName, invitation, analysisText;
        private readonly WarmButton create, join, copyInvite, ready, start, pass, matchUndo, resign, setResult, studyCopy;
        private readonly WarmButton previous, next, jump, original, studyPass, studyUndo, finishSetup, cancelSetup, blackNext, whiteNext, import, clear;
        private readonly WarmButton accept, decline, withdraw, save, saveMatch, review, analyze, stop, selectRegion, clearRegion, share;
        private readonly BoardControl board;
        private OnlineSession session;
        private KataGoEngine engine;
        private AnalysisResult localAnalysis;
        private long analysisPosition = -1, displayedPosition = -1, renderedRevision;
        private int analysisGeneration;
        private bool opening, thinking, closing, returningToReview, lastEditing;
        private GoGame displayGame;
        private StudyRegion localRegion;

        public GoGame ReviewGame { get; private set; }

        public OnlineRoomForm(EngineSettings settings, long searchVisits)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            engineSettings = settings.Clone();
            visits = searchVisits > 0 ? searchVisits : 2048;
            sound = new StoneSound(AudioSettings.LoadVolume());
            SuspendLayout();
            Name = "OnlineRoomForm";
            Text = "梓懿围棋 · 好友联机";
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = new Font("Microsoft YaHei UI", 9);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 10, 14, 9) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            var header = new Panel { Dock = DockStyle.Fill };
            title = Caption("好友联机", 32);
            title.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
            title.ForeColor = UiTheme.Text;
            subtitle = Caption("通过 Tailscale 或同一局域网，创建切磋房或研讨房。", 29);
            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            status = Caption("选择开房，或粘贴好友发来的邀请。", 36);
            status.Name = "OnlineStatus";
            status.Margin = new Padding(0, 4, 0, 0);
            var content = new Panel { Dock = DockStyle.Fill };
            shell.Controls.Add(header, 0, 0);
            shell.Controls.Add(content, 0, 1);
            shell.Controls.Add(status, 0, 2);
            Controls.Add(shell);

            lobby = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Name = "OnlineLobby" };
            var lobbyColumns = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            lobbyColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lobbyColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lobby.Controls.Add(lobbyColumns);
            TableLayoutPanel hostRows, joinRows;
            WarmCard hostCard = Card("创建房间", "plus", out hostRows);
            WarmCard joinCard = Card("加入房间", "paste", out joinRows);
            hostCard.Margin = new Padding(0, 0, 6, 0);
            joinCard.Margin = new Padding(6, 0, 0, 0);
            lobbyColumns.Controls.Add(hostCard, 0, 0);
            lobbyColumns.Controls.Add(joinCard, 1, 0);

            hostName = Input("房主", 24, "OnlineHostName");
            network = Choice("OnlineNetwork", "Tailscale 异地", "局域网");
            address = Choice("OnlineAddress");
            port = Number(1024, 65535, 37162, 1, 0);
            port.Name = "OnlinePort";
            addressHelp = Caption("", 38);
            mode = Choice("OnlineMode", "切磋 · 真人对弈", "研讨 · 共同摆棋和 AI 分析");
            size = Choice("OnlineBoardSize", "19 路", "13 路", "9 路");
            rules = Choice("OnlineRules", "中国规则", "日本规则");
            komi = Number(-150, 150, 7.5m, .5m, 1);
            hostColor = Choice("OnlineHostColor", "房主执黑", "房主执白");
            handicap = Number(0, 9, 0, 1, 0);
            handicapColor = Choice("OnlineHandicapColor", "黑方先摆", "白方先摆");
            create = ActionButton("创建并等待好友", "OnlineCreate", async delegate { await OpenRoomAsync(true); }, true);
            Add(hostRows, Field("昵称", hostName), Field("网络", network), Field("房主地址", address),
                Row(Field("TCP 端口", port), ActionButton("刷新地址", "OnlineRefreshAddresses", delegate { RefreshAddresses(); })), addressHelp,
                Field("房间类型", mode), Row(Field("棋盘", size), Field("规则", rules)),
                Row(Field("贴目", komi), Field("执色", hostColor)), Row(Field("让子数", handicap), handicapColor), create);
            network.SelectedIndexChanged += delegate { RefreshAddresses(); };

            guestName = Input("好友", 24, "OnlineGuestName");
            invitation = Input("", 1024, "OnlineInvitation");
            invitation.Multiline = true;
            invitation.Height = 88;
            invitation.ScrollBars = ScrollBars.Vertical;
            invitation.AccessibleName = "房间邀请";
            join = ActionButton("加入好友房", "OnlineJoin", async delegate { await OpenRoomAsync(false); }, true);
            Add(joinRows, Field("昵称", guestName), Caption("把好友复制的房间邀请粘贴到这里。", 28), invitation, join,
                Caption("异地对弈前先在 Tailscale 连通房主设备。局域网模式需要两台电脑处在可互通的网络。", 68),
                Caption("连接失败时请核对房主是否仍开着房间，以及防火墙是否允许该程序在所选网络接收连接。", 67));
            invitation.TextChanged += delegate { UpdateLobbyButtons(); };

            room = new Panel { Dock = DockStyle.Fill, Visible = false, Name = "OnlineWorkspace" };
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            room.Controls.Add(columns);
            var boardPane = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), BackColor = UiTheme.Surface };
            board = new BoardControl { Name = "OnlineBoard", Dock = DockStyle.Fill, BackColor = UiTheme.Surface, AllowInput = false };
            positionInfo = Caption("", 31);
            positionInfo.Padding = new Padding(10, 0, 8, 0);
            var boardFooter = new Panel { Dock = DockStyle.Bottom, Padding = new Padding(10, 0, 10, 0), Height = 31, BackColor = UiTheme.Surface };
            var numbers = new WarmToggle { Text = "显示手数", Name = "OnlineMoveNumbers", Dock = DockStyle.Left, Width = 116 };
            numbers.CheckedChanged += delegate { board.ShowMoveNumbers = numbers.Checked; };
            boardFooter.Controls.Add(numbers);
            boardPane.Controls.Add(board);
            boardPane.Controls.Add(boardFooter);
            boardPane.Controls.Add(positionInfo);
            columns.Controls.Add(boardPane, 0, 0);
            board.PointClicked += OnBoardPoint;
            board.RegionSelected += delegate(object sender, StudyRegionEventArgs e) { localRegion = e.Region; ResetAnalysis(); UpdateRoom(); };

            var sideScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            var sideRows = Stack();
            sideScroll.Controls.Add(sideRows);
            columns.Controls.Add(sideScroll, 1, 0);

            TableLayoutPanel roomRows, matchRows, studyRows, proposalRows, analysisRows;
            WarmCard roomCard = Card("房间与连接", "game", out roomRows);
            roomInfo = Caption("", 66);
            copyInvite = ActionButton("复制邀请", "OnlineCopyInvitation", CopyInvitation);
            ready = CommandButton("我已准备", "OnlineReady", "ready");
            start = CommandButton("开始切磋", "OnlineStart", "start", true);
            save = ActionButton("保存棋谱", "OnlineSaveSgf", delegate { SaveGame(false); });
            saveMatch = ActionButton("保存原对局", "OnlineSaveOriginal", delegate { SaveGame(true); });
            review = ActionButton("带入本地复盘", "OnlineReview", EnterLocalReview);
            Add(roomRows, roomInfo, Row(copyInvite, ActionButton("离开房间", "OnlineLeave", delegate { Close(); })),
                Row(ready, start), Row(save, review), saveMatch);

            matchActions = Card("切磋操作", "play", out matchRows);
            pass = CommandButton("停一手", "OnlinePass", "pass");
            matchUndo = CommandButton("申请悔棋", "OnlineUndo", "undo");
            resign = ActionButton("认输", "OnlineResign", Resign);
            setResult = ActionButton("确认终局结果", "OnlineResult", ChooseResult);
            studyCopy = CommandButton("复制到研讨桌", "OnlineStudyCopy", "study-copy", true);
            Add(matchRows, Caption("切磋期间关闭 AI。对局结束后可一起研讨。", 32), Row(pass, matchUndo, resign), Row(setResult, studyCopy));

            proposalCard = Card("双方确认", "check", out proposalRows);
            proposalText = Caption("", 55);
            accept = ActionButton("同意", "OnlineAccept", delegate { Respond(true); }, true);
            decline = ActionButton("拒绝", "OnlineDecline", delegate { Respond(false); });
            withdraw = CommandButton("撤回请求", "OnlineWithdraw", "cancel-proposal");
            Add(proposalRows, proposalText, Row(accept, decline, withdraw));

            studyActions = Card("共同摆棋与试下", "board", out studyRows);
            studyTool = Choice("OnlineStudyTool", "试下 · 按轮次", "摆黑子", "摆白子", "擦除");
            studyTool.SelectedIndexChanged += delegate { if (session != null) UpdateRoom(); };
            blackNext = ActionButton("设黑先", "OnlineBlackNext", delegate { SetSetupPlayer(Stone.Black); });
            whiteNext = ActionButton("设白先", "OnlineWhiteNext", delegate { SetSetupPlayer(Stone.White); });
            finishSetup = CommandButton("完成摆棋", "OnlineFinishSetup", "finish-setup", true);
            cancelSetup = CommandButton("取消摆棋", "OnlineCancelSetup", "cancel-setup");
            previous = ActionButton("上一手", "OnlinePrevious", delegate { Navigate(-1); });
            next = ActionButton("下一手", "OnlineNext", delegate { Navigate(1); });
            jumpMove = Number(0, 0, 0, 1, 0);
            jumpMove.Name = "OnlineJumpMove";
            jump = ActionButton("跳转", "OnlineJump", delegate { Send(new OnlineCommand { Kind = "navigate", Position = (int)jumpMove.Value }); });
            original = CommandButton("切回原谱", "OnlineOriginal", "original");
            studyPass = CommandButton("试下停着", "OnlineStudyPass", "pass");
            studyUndo = CommandButton("撤销", "OnlineStudyUndo", "undo");
            import = ActionButton("导入 SGF", "OnlineImportSgf", ImportGame);
            clear = CommandButton("清空棋盘", "OnlineClear", "clear");
            Add(studyRows, studyTool, Row(blackNext, whiteNext, studyUndo), Row(finishSetup, cancelSetup),
                Row(previous, next, original), Row(Field("手数", jumpMove), jump), Row(studyPass, import, clear));

            analysisCard = Card("研讨 AI", "engine", out analysisRows);
            engineInfo = Caption(engineSettings.Description, 40);
            analyze = ActionButton("计算当前局面", "OnlineAnalyze", async delegate { await AnalyzeAsync(); }, true);
            stop = ActionButton("停止", "OnlineStopAnalysis", delegate { ResetAnalysis(); UpdateRoom(); });
            selectRegion = ActionButton("框选局部", "OnlineSelectRegion", StartRegionSelection);
            clearRegion = ActionButton("全盘", "OnlineClearRegion", delegate { board.SelectingRegion = false; localRegion = null; analysisChoice.SelectedIndex = 0; ResetAnalysis(); UpdateRoom(); });
            share = ActionButton("共享我的分析", "OnlineShareAnalysis", ShareAnalysis);
            analysisChoice = Choice("OnlineAnalysisSource", "我的计算", "房主共享", "好友共享");
            analysisChoice.SelectedIndexChanged += delegate { if (session != null) ShowAnalysis(session.State); };
            analysisText = Input("", 5000, "OnlineAnalysisText");
            analysisText.ReadOnly = true;
            analysisText.Multiline = true;
            analysisText.ScrollBars = ScrollBars.Vertical;
            analysisText.Height = 137;
            Add(analysisRows, engineInfo, Row(analyze, stop), Row(selectRegion, clearRegion), Row(analysisChoice, share), analysisText);
            Add(sideRows, roomCard, proposalCard, matchActions, studyActions, analysisCard);

            content.Controls.Add(room);
            content.Controls.Add(lobby);
            RefreshAddresses();
            if (address.Items.Count == 0) network.SelectedIndex = 1;
            UiTheme.FinishDpiLayout(this, new Size(1140, 745));
        }

        private static TableLayoutPanel Stack()
        {
            var table = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static WarmCard Card(string title, string icon, out TableLayoutPanel rows)
        {
            var card = new WarmCard { Title = title, IconName = icon, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            rows = Stack();
            rows.BackColor = UiTheme.Surface;
            card.Controls.Add(rows);
            return card;
        }

        private static void Add(TableLayoutPanel table, params Control[] controls)
        {
            foreach (Control control in controls)
            {
                int row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                control.Dock = DockStyle.Top;
                table.Controls.Add(control, 0, row);
            }
        }

        private static TableLayoutPanel Row(params Control[] controls)
        {
            var table = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            for (int i = 0; i < controls.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
                controls[i].Dock = DockStyle.Top;
                table.Controls.Add(controls[i], i, 0);
            }
            return table;
        }

        private static TableLayoutPanel Field(string title, Control input)
        {
            var table = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.Controls.Add(Caption(title, 26), 0, 0);
            input.AccessibleName = title;
            input.Dock = DockStyle.Top;
            table.Controls.Add(input, 1, 0);
            return table;
        }

        private static Label Caption(string text, int height)
        {
            return new Label { Dock = DockStyle.Top, Height = height, Text = text, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted };
        }

        private static ComboBox Choice(string name, params string[] items)
        {
            var combo = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(items);
            if (items.Length > 0) combo.SelectedIndex = 0;
            return combo;
        }

        private static TextBox Input(string value, int limit, string name)
        {
            return new TextBox { Name = name, Text = value, MaxLength = limit, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step, int places)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = step, DecimalPlaces = places, BorderStyle = BorderStyle.FixedSingle };
        }

        private static WarmButton ActionButton(string text, string name, Action action, bool primary = false)
        {
            var button = new WarmButton { Text = text, Name = name, Height = 29, Primary = primary };
            button.Click += delegate { action(); };
            return button;
        }

        private WarmButton CommandButton(string text, string name, string kind, bool primary = false)
        {
            return ActionButton(text, name, delegate { Send(new OnlineCommand { Kind = kind }); }, primary);
        }

        private void RefreshAddresses()
        {
            IPAddress previousAddress = address.SelectedItem is AddressChoice ? ((AddressChoice)address.SelectedItem).Address : null;
            address.Items.Clear();
            bool tailscale = network.SelectedIndex == 0;
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    bool tailAdapter = adapter.Name.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        adapter.Description.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) >= 0;
                    foreach (UnicastIPAddressInformation item in adapter.GetIPProperties().UnicastAddresses)
                    {
                        IPAddress ip = item.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                        byte[] b = ip.GetAddressBytes();
                        bool tailIp = b[0] == 100 && b[1] >= 64 && b[1] <= 127;
                        bool localIp = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        if ((tailscale && tailIp && tailAdapter) || (!tailscale && localIp && !tailAdapter))
                            address.Items.Add(new AddressChoice { Address = ip, InterfaceName = adapter.Name });
                    }
                }
            }
            catch (NetworkInformationException error) { status.Text = "无法读取网络地址：" + error.Message; }
            if (address.Items.Count > 0)
            {
                int old = address.Items.Cast<AddressChoice>().ToList().FindIndex(choice => choice.Address.Equals(previousAddress));
                address.SelectedIndex = Math.Max(0, old);
            }
            addressHelp.Text = address.Items.Count > 0 ? "房间仅在所选地址监听。" : tailscale
                ? "请先登录并连接 Tailscale，再刷新地址。" : "未找到局域网地址。连接 Wi-Fi 或网线后再刷新。";
            UpdateLobbyButtons();
        }

        private void UpdateLobbyButtons()
        {
            create.Enabled = !opening && address.SelectedItem != null;
            join.Enabled = !opening && !String.IsNullOrWhiteSpace(invitation.Text);
        }

        private async Task OpenRoomAsync(bool host)
        {
            if (opening || session != null) return;
            opening = true;
            UpdateLobbyButtons();
            status.Text = host ? "正在创建房间…" : "正在连接房主…";
            try
            {
                OnlineSession created;
                if (host)
                {
                    var endpoint = address.SelectedItem as AddressChoice;
                    if (endpoint == null) throw new InvalidOperationException("请选择房主网络地址。");
                    int tcpPort = (int)port.Value;
                    OnlineRoomMode roomMode = mode.SelectedIndex == 0 ? OnlineRoomMode.Match : OnlineRoomMode.Study;
                    Stone color = hostColor.SelectedIndex == 0 ? Stone.Black : Stone.White;
                    string name = PlayerName(hostName.Text, "房主");
                    var initial = new GoGame();
                    initial.NewGame(size.SelectedIndex == 0 ? 19 : size.SelectedIndex == 1 ? 13 : 9, (double)komi.Value, (int)handicap.Value,
                        handicapColor.SelectedIndex == 0 ? Stone.Black : Stone.White);
                    initial.Rules = rules.SelectedIndex == 0 ? "chinese-ogs" : "japanese";
                    created = await Task.Run(() => OnlineSession.Host(endpoint.Address, tcpPort, roomMode, color, initial, name));
                }
                else created = await OnlineSession.JoinAsync(invitation.Text.Trim(), PlayerName(guestName.Text, "好友"));
                if (closing || IsDisposed) { created.Dispose(); return; }
                session = created;
                session.Changed += OnRoomChanged;
                lobby.Visible = false;
                room.Visible = true;
                status.Text = host ? "房间已创建，复制邀请发给好友。" : "已加入房间。";
                UpdateRoom();
            }
            catch (Exception error)
            {
                if (!closing) status.Text = (host ? "开房失败：" : "加入失败：") + error.Message;
            }
            finally
            {
                opening = false;
                if (!closing) UpdateLobbyButtons();
            }
        }

        private static string PlayerName(string value, string fallback)
        {
            value = value.Trim();
            return value.Length == 0 ? fallback : value;
        }

        private void OnRoomChanged()
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)UpdateRoom); }
            catch (InvalidOperationException) { }
        }

        private void UpdateRoom()
        {
            if (session == null || closing) return;
            OnlineRoomState state = session.State;
            if (state == null) return;
            renderedRevision = state.Revision;
            bool study = state.Mode == OnlineRoomMode.Study;
            bool isHost = session.Seat == 0;
            bool final = !String.IsNullOrEmpty(state.Result);
            bool active = state.Connected && state.Started;
            bool pending = state.Proposal != null;
            bool canAct = active && !pending;
            if (displayedPosition != state.PositionRevision)
            {
                int previousPosition = displayGame == null ? -1 : displayGame.Position;
                int previousSize = displayGame == null ? -1 : displayGame.Size;
                ResetAnalysis();
                displayGame = state.GetGame();
                if (displayGame.Size != previousSize) localRegion = null;
                displayedPosition = state.PositionRevision;
                board.Game = displayGame;
                jumpMove.Maximum = displayGame.Moves.Count;
                jumpMove.Value = displayGame.Position;
                if (lastEditing && !state.Editing) studyTool.SelectedIndex = 0;
                lastEditing = state.Editing;
                if (previousPosition >= 0 && displayGame.Position == previousPosition + 1 && !displayGame.Moves[previousPosition].IsPass)
                    sound.Play();
            }
            if (displayGame == null) return;
            Stone myColor = isHost ? state.HostColor : GoGame.Opponent(state.HostColor);
            bool myTurn = displayGame.ToPlay == myColor;
            bool liveMatch = canAct && !study && !final;
            string peer = isHost ? state.GuestName : state.HostName;
            title.Text = study ? "好友研讨房" : "好友切磋房";
            subtitle.Text = study ? "两边都可摆棋、试下与分享本机 AI 分析。" : "双方轮流落子，结果确认后可一起进入研讨。";
            string connection = state.Connected ? "已连接" : state.Started ? "连接中断，等待好友重连" : "等待好友加入";
            roomInfo.Text = connection + " · " + (isHost ? "我是房主" : "我是加入者") +
                (String.IsNullOrEmpty(peer) ? "" : "\r\n对方 " + peer) + (study ? "" : " · 我执" + ColorName(myColor));
            positionInfo.Text = "第 " + displayGame.Position + " / " + displayGame.Moves.Count + " 手 · " + displayGame.Size + " 路 · 贴目 " +
                displayGame.Komi.ToString("0.#", CultureInfo.InvariantCulture) + " · " + (displayGame.Rules == "japanese" ? "日本规则" : "中国规则") + " · " +
                (state.Editing ? "摆棋中，下手 " + ColorName(state.SetupPlayer) : final ? "结果 " + state.Result : displayGame.IsOver ? "双方停着" : ColorName(displayGame.ToPlay) + "方行棋");
            copyInvite.Visible = isHost && String.IsNullOrEmpty(state.GuestName);
            ready.Visible = !study && !isHost && !state.Started;
            ready.Enabled = state.Connected && !state.GuestReady;
            ready.Text = state.GuestReady ? "已准备" : "我已准备";
            start.Visible = !study && isHost && !state.Started;
            start.Enabled = state.Connected && state.GuestReady;
            matchActions.Visible = !study && state.Started;
            studyActions.Visible = study && state.Started;
            analysisCard.Visible = study && state.Started;
            proposalCard.Visible = pending;
            if (pending) UpdateProposal(state.Proposal, state.Connected);

            pass.Enabled = liveMatch && myTurn && !displayGame.IsOver;
            matchUndo.Enabled = liveMatch && displayGame.Position > 0;
            resign.Enabled = liveMatch;
            setResult.Enabled = liveMatch && displayGame.IsOver;
            studyCopy.Enabled = canAct && final;
            board.AllowInput = study ? canAct && (studyTool.SelectedIndex > 0 || (!displayGame.IsOver && !state.Editing)) : pass.Enabled;
            blackNext.Enabled = whiteNext.Enabled = import.Enabled = clear.Enabled = canAct;
            finishSetup.Enabled = cancelSetup.Enabled = canAct && state.Editing;
            studyUndo.Enabled = canAct && (state.Editing || displayGame.Position > 0);
            previous.Enabled = canAct && !state.Editing && displayGame.Position > 0;
            next.Enabled = canAct && !state.Editing && displayGame.Position < displayGame.Moves.Count;
            original.Enabled = canAct && !state.Editing && state.OriginalAvailable;
            jump.Enabled = jumpMove.Enabled = canAct && !state.Editing && displayGame.Moves.Count > 0;
            studyPass.Enabled = canAct && !state.Editing && !displayGame.IsOver;
            save.Enabled = state.Started;
            saveMatch.Visible = !String.IsNullOrEmpty(state.OriginalMatchSgf);
            review.Enabled = state.Started && (study || final);

            bool mayAnalyze = canAct && study && !state.Editing;
            if (!mayAnalyze && thinking) ResetAnalysis();
            analyze.Enabled = selectRegion.Enabled = clearRegion.Enabled = mayAnalyze && !thinking;
            stop.Enabled = thinking;
            share.Enabled = mayAnalyze && localAnalysis != null && analysisPosition == state.PositionRevision;
            if (!mayAnalyze && board.SelectingRegion) board.SelectingRegion = false;
            ShowAnalysis(state);
            if (!String.IsNullOrWhiteSpace(session.LastError)) status.Text = session.LastError;
            else if (!state.Connected) status.Text = connection;
            else if (pending) status.Text = "等待对方确认。";
            else if (!state.Started) status.Text = state.GuestReady ? "好友已准备，房主可以开始。" : "双方检查棋盘设置后开始。";
            else if (!study) status.Text = final ? "对局已结束。" : displayGame.IsOver ? "双方连续停着，请确认终局结果。" : myTurn ? "轮到你了。" : "等待好友落子。";
            else status.Text = state.Editing ? "摆棋完成后点「完成摆棋」，再试下或计算。" : "选择工具后点击棋盘，两边可共同操作。";
        }

        private static string ColorName(Stone color) { return color == Stone.Black ? "黑" : "白"; }

        private void UpdateProposal(OnlineProposal proposal, bool connected)
        {
            bool mine = proposal.Seat == session.Seat;
            string action;
            switch (proposal.Kind)
            {
                case "undo": action = "撤回最后一手"; break;
                case "result": action = "记录结果 " + proposal.Text; break;
                case "clear": action = "清空棋盘"; break;
                case "import": action = "替换为导入的棋谱"; break;
                case "study-copy": action = "把对局复制到研讨桌"; break;
                default: action = "更新棋局"; break;
            }
            proposalText.Text = (mine ? "你申请" : "好友申请") + action + "。";
            accept.Enabled = decline.Enabled = connected && !mine;
            withdraw.Enabled = connected && mine;
        }

        private void OnBoardPoint(object sender, BoardPointEventArgs point)
        {
            if (session == null) return;
            OnlineRoomState state = session.State;
            if (state == null || !board.AllowInput || state.PositionRevision != displayedPosition) return;
            int tool = studyTool.SelectedIndex;
            if (state.Mode == OnlineRoomMode.Study && tool > 0)
                Send(new OnlineCommand { Kind = "edit", X = point.X, Y = point.Y, Color = tool == 1 ? Stone.Black : tool == 2 ? Stone.White : Stone.Empty });
            else Send(new OnlineCommand { Kind = "move", X = point.X, Y = point.Y });
        }

        private void Send(OnlineCommand command)
        {
            if (session == null || closing) return;
            if (command.Revision == 0) command.Revision = renderedRevision;
            try { session.Send(command); }
            catch (Exception error) { status.Text = error.Message; }
        }

        private void Respond(bool acceptProposal) { Send(new OnlineCommand { Kind = "respond", Accept = acceptProposal }); }
        private void SetSetupPlayer(Stone color) { Send(new OnlineCommand { Kind = "setup-player", Color = color }); }

        private void Navigate(int delta)
        {
            if (displayGame != null) Send(new OnlineCommand { Kind = "navigate", Position = displayGame.Position + delta });
        }

        private void CopyInvitation()
        {
            if (session == null || session.Seat != 0) return;
            try { Clipboard.SetText(session.Invitation); status.Text = "邀请已复制，可以发给好友。"; }
            catch (Exception error) { status.Text = "无法复制邀请：" + error.Message; }
        }

        private void Resign()
        {
            long revision = renderedRevision;
            if (MessageBox.Show(this, "确认认输并结束这盘棋？", "认输", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Send(new OnlineCommand { Kind = "resign", Revision = revision });
        }

        private void ChooseResult()
        {
            long revision = renderedRevision;
            using (var dialog = new Form { Text = "确认终局结果", Font = Font, Width = 340, Height = 205, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                var rows = Stack();
                rows.Padding = new Padding(10);
                var winner = Choice("OnlineResultWinner", "黑胜", "白胜", "和棋");
                var margin = Number(.5m, 999.5m, .5m, .5m, 1);
                winner.SelectedIndexChanged += delegate { margin.Enabled = winner.SelectedIndex != 2; };
                var ok = new WarmButton { Text = "提交给好友确认", DialogResult = DialogResult.OK, Primary = true };
                var cancel = new WarmButton { Text = "取消", DialogResult = DialogResult.Cancel };
                Add(rows, Field("胜负", winner), Field("领先目数", margin), Row(ok, cancel));
                dialog.Controls.Add(rows);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                UiTheme.StyleDialog(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string result = winner.SelectedIndex == 2 ? "0" : (winner.SelectedIndex == 0 ? "B+" : "W+") + margin.Value.ToString("0.#", CultureInfo.InvariantCulture);
                Send(new OnlineCommand { Kind = "result", Text = result, Revision = revision });
            }
        }

        private void ImportGame()
        {
            long revision = renderedRevision;
            using (var dialog = new OpenFileDialog { Filter = "围棋棋谱 (*.sgf)|*.sgf", Title = "导入共享棋谱" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("棋谱文件大于 1 MB。");
                    string sgf = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                    if (sgf.Length > OnlineAuthority.MaxSgfChars) throw new InvalidDataException("棋谱超过联机房间长度限制。");
                    GoGame imported = GoGame.FromSgf(sgf);
                    Send(new OnlineCommand { Kind = "import", Text = imported.ToSgf(), Revision = revision });
                }
                catch (Exception error) { status.Text = "棋谱导入失败：" + error.Message; }
            }
        }

        private void SaveGame(bool originalMatch)
        {
            if (session == null) return;
            try
            {
                string sgf = originalMatch ? session.State.OriginalMatchSgf : session.State.ExportSgf();
                if (String.IsNullOrEmpty(sgf)) return;
                using (var dialog = new SaveFileDialog { Filter = "围棋棋谱 (*.sgf)|*.sgf", FileName = originalMatch ? "原对局.sgf" : "好友棋谱.sgf", DefaultExt = "sgf" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, sgf, new UTF8Encoding(false));
                    status.Text = "棋谱已保存。";
                }
            }
            catch (Exception error) { status.Text = "保存失败：" + error.Message; }
        }

        private void EnterLocalReview()
        {
            if (session == null) return;
            OnlineRoomState state = session.State;
            if (state.Mode != OnlineRoomMode.Study && String.IsNullOrEmpty(state.Result)) return;
            if (MessageBox.Show(this, "离开房间，把这盘棋带入本地复盘？", "带入本地复盘", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                ReviewGame = GoGame.FromSgf(state.ExportSgf());
                ReviewGame.Goto(Math.Min(state.Position, ReviewGame.Moves.Count));
                returningToReview = true;
                Close();
            }
            catch (Exception error) { status.Text = "无法带入复盘：" + error.Message; }
        }

        private void StartRegionSelection()
        {
            if (!CanStudy(session == null ? null : session.State)) return;
            ResetAnalysis();
            analysisChoice.SelectedIndex = 0;
            board.SelectingRegion = true;
            status.Text = "在棋盘拖动鼠标框选局部。按 Esc 可取消。";
        }

        private static bool CanStudy(OnlineRoomState state)
        {
            return state != null && state.Mode == OnlineRoomMode.Study && state.Connected && state.Started && !state.Editing && state.Proposal == null;
        }

        private async Task AnalyzeAsync()
        {
            OnlineSession current = session;
            OnlineRoomState state = current == null ? null : current.State;
            if (!CanStudy(state) || thinking || board.SelectingRegion || displayedPosition != state.PositionRevision) return;
            long revision = state.PositionRevision;
            StudyRegion region = localRegion;
            int request = ++analysisGeneration;
            thinking = true;
            localAnalysis = null;
            analysisPosition = -1;
            UpdateRoom();
            engineInfo.Text = "正在本机计算 · " + visits.ToString("N0") + " 次搜索";
            try
            {
                if (engine == null) engine = new KataGoEngine(engineSettings);
                AnalysisResult result = await engine.AnalyzeAsync(state.GetGame(), visits, false, null, region);
                if (closing || session != current || request != analysisGeneration) return;
                OnlineRoomState now = current.State;
                if (!CanStudy(now) || now.PositionRevision != revision) return;
                localAnalysis = result;
                analysisPosition = revision;
                analysisChoice.SelectedIndex = 0;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!closing && request == analysisGeneration) status.Text = "AI 分析失败：" + error.Message; }
            finally
            {
                if (!closing && request == analysisGeneration)
                {
                    thinking = false;
                    engineInfo.Text = engineSettings.Description;
                    ShowAnalysis(current.State);
                    analyze.Enabled = selectRegion.Enabled = clearRegion.Enabled = CanStudy(current.State);
                    stop.Enabled = false;
                    share.Enabled = localAnalysis != null && CanStudy(current.State);
                }
            }
        }

        private void ResetAnalysis()
        {
            analysisGeneration++;
            if (thinking && engine != null) engine.Cancel();
            thinking = false;
            localAnalysis = null;
            analysisPosition = -1;
            board.Analysis = null;
            board.ShowHints = false;
            engineInfo.Text = engineSettings.Description;
        }

        private void ShareAnalysis()
        {
            OnlineRoomState state = session == null ? null : session.State;
            if (!CanStudy(state) || localAnalysis == null || state.PositionRevision != analysisPosition || displayGame == null) return;
            AnalysisResult result = localAnalysis;
            var candidates = result.Candidates.Take(5).ToArray();
            Send(new OnlineCommand
            {
                Kind = "share", PositionRevision = analysisPosition,
                Analysis = new OnlineSharedAnalysis
                {
                    Model = engineSettings.Description, Visits = result.Visits, BlackWinrate = result.BlackWinrate, BlackScoreLead = result.BlackScoreLead,
                    GtpMoves = candidates.Select(move => move.Move).ToArray(),
                    Candidates = candidates.Select((move, index) => CandidateLine(move, displayGame.Size, index)).ToArray(),
                    Region = result.Region == null ? "全盘" : result.Region.Describe(displayGame.Size)
                }
            });
        }

        private void ShowAnalysis(OnlineRoomState state)
        {
            if (state == null || state.Mode != OnlineRoomMode.Study || state.Editing || displayGame == null || state.PositionRevision != displayedPosition)
            {
                board.StudyRegion = null;
                board.Analysis = null;
                board.ShowHints = false;
                analysisText.Text = "";
                return;
            }
            if (analysisChoice.SelectedIndex == 0)
            {
                board.StudyRegion = localRegion;
                AnalysisResult result = analysisPosition == state.PositionRevision ? localAnalysis : null;
                board.Analysis = result;
                board.ShowHints = result != null;
                analysisText.Text = result == null ? "点击计算获取推荐点，或查看好友共享。" :
                    FormatAnalysis(engineSettings.Description, result.Visits, result.BlackWinrate, result.BlackScoreLead,
                        result.Region == null ? "全盘" : result.Region.Describe(displayGame.Size),
                        result.Candidates.Take(5).Select((candidate, index) => CandidateLine(candidate, displayGame.Size, index)).ToArray());
                return;
            }
            OnlineSharedAnalysis shared = analysisChoice.SelectedIndex == 1 ? state.HostAnalysis : state.GuestAnalysis;
            board.StudyRegion = null;
            board.Analysis = shared == null || shared.GtpMoves == null ? null : new AnalysisResult
            {
                Candidates = shared.GtpMoves.Select(move => new MoveCandidate { Move = move }).ToList(),
                BlackWinrate = shared.BlackWinrate, BlackScoreLead = shared.BlackScoreLead, Visits = shared.Visits
            };
            board.ShowHints = board.Analysis != null;
            analysisText.Text = shared == null ? "这位棋友尚未共享当前局面的分析。" :
                FormatAnalysis(shared.Model, shared.Visits, shared.BlackWinrate, shared.BlackScoreLead, shared.Region, shared.Candidates);
        }

        private static string FormatAnalysis(string model, long visits, double winrate, double score, string region, string[] candidates)
        {
            return model + " · " + visits.ToString("N0") + " 次搜索 · " + region + "\r\n黑胜率 " + winrate.ToString("P1") +
                " · 黑方目差 " + score.ToString("+0.0;-0.0;0.0") + " 目\r\n" + String.Join("\r\n", candidates ?? new string[0]);
        }

        private static string CandidateLine(MoveCandidate candidate, int boardSize, int index)
        {
            string[] pv = candidate.Pv ?? new string[0];
            int skip = pv.Length > 0 && String.Equals(pv[0], candidate.Move, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            string trail = String.Join(" ", pv.Skip(skip).Take(7).Select(move => DisplayPoint(move, boardSize)));
            return (char)('A' + index) + " " + DisplayPoint(candidate.Move, boardSize) + " · " + candidate.Winrate.ToString("P1") + " · " + candidate.ScoreLead.ToString("+0.0;-0.0;0.0") + " 目" +
                (trail.Length > 0 ? " · " + trail : "");
        }

        private static string DisplayPoint(string move, int boardSize)
        {
            try { return GoGame.ParseGtp(move, Stone.Black, boardSize).ToDisplay(boardSize); }
            catch (FormatException) { return move; }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape && board.SelectingRegion)
            {
                board.SelectingRegion = false;
                UpdateRoom();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!returningToReview && !closing && session != null && session.State != null && session.State.Started && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, "离开房间前可以先保存棋谱。现在离开？", "离开好友房", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
            base.OnFormClosing(e);
            if (!e.Cancel) CloseResources();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseResources();
            base.Dispose(disposing);
        }

        private void CloseResources()
        {
            if (closing) return;
            closing = true;
            if (session != null)
            {
                session.Changed -= OnRoomChanged;
                session.Dispose();
                session = null;
            }
            if (engine != null) { engine.Dispose(); engine = null; }
            sound.Dispose();
        }
    }
}
