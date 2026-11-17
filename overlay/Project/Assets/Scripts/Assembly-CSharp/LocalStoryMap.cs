using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The story catalog supplies chapter order and progression. This view owns
// only presentation, navigation and the position of the chapter camera.
internal sealed class LocalStoryMap : Dialog
{
    private const float HeaderHeight = 83f;
    private const float FooterHeight = 64f;
    private const float DrawerWidth = 388f;
    private const float StageSpacing = 240f;
    private const float RouteMargin = 155f;
    private const int ChaptersPerPage = 5;
    private static readonly string[] Lines = { "Character", "Weapon", "Outfit" };
    private static readonly string[] LineNames = { "角色故事", "专武支线", "助手服装" };
    private static readonly float[] RouteHeights = { -47, 35, 93, 18, -64, -15, 66, 19, -64 };
    private static readonly Color Ink = new Color32(23, 34, 36, 255);
    private static readonly Color PanelInk = new Color32(34, 51, 53, 255);
    private static readonly Color Paper = new Color32(245, 235, 206, 255);
    private static readonly Color Gold = new Color32(224, 191, 122, 255);
    private static readonly Color Dim = new Color32(181, 192, 184, 255);
    private static readonly Color Jade = new Color32(181, 233, 193, 255);

    private sealed class Chapter
    {
        internal readonly LocalStoryChapter[] Stages;
        internal Chapter(IEnumerable<LocalStoryChapter> stages) => Stages = stages.OrderBy(stage => stage.Id).ToArray();
        internal string Title => string.IsNullOrEmpty(Stages[0].StoryTitle) ? Stages[0].MenuLabel : Stages[0].StoryTitle;
        internal string Line => Stages[0].Line;
        internal int Cleared => Stages.Count(stage => LocalStoryMenu.Progress(stage.Id)?.stars > 0);
    }

    private UIFont font;
    private UIAtlas atlas;
    private UIAtlas bossAtlas;
    private Material solid;
    private Camera uiCamera;
    private Chapter[] chapters;
    private Chapter current;
    private Transform layout, map, route, drawer, chrome, header, footer;
    private UITexture landscape, headerSurface, footerSurface, scrollThumb;
    private Texture2D defaultLandscape;
    private BoxCollider pageHit, panHit, drawerHit, sidebarHit;
    private UITexture drawerShade, drawerEdge, drawerSurface;
    private Transform sidebar, chapterEntries, chapterPaging;
    private Transform backButton;
    private UILabel heading, completion, drawerButton, range;
    private GameObject previousMap, nextMap, scrollTrack;
    private float offset, limit, routeWidth, mapWidth, mapHeight;
    private int listPage;
    private int drawerLine;
    private bool expanded;
    private UIRoot uiRoot;
    private Rect lastCameraPixels, lastSafePixels;
    private int canvasRevision = -1;
    private Vector2 stageInsets;

    internal int FirstStage => current.Stages[0].Id;
    internal int FocusStage { get; private set; }

    internal static void Open(string line, LocalStoryChapter focus = null)
    {
        if (!LocalStoryMenu.RequireOpen()) return;
        var chapters = LocalStoryChapter.All.OrderBy(stage => stage.Id)
            .GroupBy(stage => (stage.Line, Key: string.IsNullOrEmpty(stage.StoryId) ? stage.Id.ToString() : stage.StoryId, stage.Act))
            .Select(group => new Chapter(group)).ToArray();
        var chapter = focus == null ? chapters.FirstOrDefault(row => row.Line == line) :
            chapters.FirstOrDefault(row => row.Stages.Contains(focus));
        if (chapter == null) return;
        var manager = GameObject.FindGameObjectWithTag("PageRoot")?.GetComponent<DialogManager>();
        if (manager == null) return;
        var existing = manager.GetDialog<LocalStoryMap>(DialogManager.SpaceCurrent);
        if (existing != null && !existing.GonnaHide)
        {
            existing.SetChapter(chapter.Stages[0].Id, focus?.Id ?? 0);
            return;
        }

        var space = manager.GetCurrentSpace(DialogType.PAGE, DialogManager.SpaceCurrent);
        int index = DialogManager.ChildrenCount(space);
        var page = Child(space.transform, "LocalStoryMap", 0, 0,
            index * DialogManager.SPACE_OF_EACH_DIALOG - 50).gameObject;
        page.layer = LayerMask.NameToLayer("GUI");
        var view = page.AddComponent<LocalStoryMap>();
        view.Type = DialogType.PAGE;
        view.SpaceID = manager.CurActiveSpaceID;
        view.PositionIndex = index;
        view.GuiHandler = new DialogGUIHandler();
        view.SetHud(false, false, false, false, false, false, false);
        view.SetGNB(false);
        view.Initialize(chapters, chapter, focus?.Id ?? 0);
        view.DoShow();
        LocalUiPageCanvas.SuppressTownChrome(view);
    }

    internal static void CloseForBattlePreview()
    {
        Dialog.GetDialog<LocalStoryMap>(DialogManager.SpaceCurrent)?.DoHide();
    }

    private void Initialize(Chapter[] rows, Chapter chapter, int focus)
    {
        font = Resources.Load<GameObject>("fonts/NotoSansBold_30").GetComponent<UIFont>();
        atlas = Resources.Load<GameObject>("atlas/UI_Atlas")?.GetComponent<UIAtlas>();
        bossAtlas = Resources.Load<GameObject>("atlas/UI_Atlas_02")?.GetComponent<UIAtlas>();
        solid = new Material(Shader.Find("Unlit/Transparent Colored"))
        {
            hideFlags = HideFlags.DontSave,
            mainTexture = Texture2D.whiteTexture
        };
        chapters = rows;
        uiRoot = NGUITools.FindInParents<UIRoot>(gameObject);
        uiCamera = NGUITools.FindCameraForLayer(gameObject.layer);
        layout = Child(transform, "StoryLayout");
        LocalUiViewport.Content(layout.gameObject);
        defaultLandscape = Resources.Load<Texture2D>("texture/bg/BG_GuildBattleMap");
        var scenery = Panel(layout, "MapLandscape", 0, 0, -15);
        landscape = RectWidget(scenery, 0, 0, 1, 1, Color.white, 0, defaultLandscape);
        pageHit = InputSurface(scenery, "PageSurface", 0, 0, 1, 1, 0).GetComponent<BoxCollider>();
        map = Panel(layout, "MapWindow", 0, 0, -25);
        var viewport = map.GetComponent<UIPanel>();
        viewport.clipping = UIDrawCall.Clipping.SoftClip;
        viewport.clipSoftness = new Vector2(5, 1);
        panHit = PanSurface(map, "PanSurface", 0, 0, 1, 1, -1).GetComponent<BoxCollider>();
        route = Child(map, "Route");
        chrome = Panel(layout, "MapControls", 0, 0, -52);
        headerSurface = RectWidget(chrome, 0, 0, 1, 1, new Color(.06f, .11f, .12f, .92f), 0);
        footerSurface = RectWidget(chrome, 0, 0, 1, 1, new Color(.06f, .11f, .12f, .92f), 0);
        header = Child(chrome, "Header");
        footer = Child(chrome, "Footer");
        backButton = Child(header, "CloseMap");
        Sprite(backButton, atlas, "UI_Btn_Back", 0, 0, 55, 54, 8);
        InputSurface(backButton, "Hit", 0, 0, 62, 60, -2).onClick = _ => DoHide();
        drawerButton = Button(header, "ChapterToggle", 0, 0, 132, 43, "章节  ▸", ToggleDrawer);
        heading = Label(header, "ChapterTitle", 0, 10, 28, "", Paper, 610);
        completion = Label(header, "ChapterProgress", 0, -21, 16, "", Dim);
        range = Label(footer, "RouteRange", 0, -9, 16, "", Dim);
        scrollTrack = Child(footer, "RouteTrack", 0, 12).gameObject;
        RectWidget(scrollTrack.transform, 0, 0, 255, 4, PanelInk, 1);
        scrollThumb = RectWidget(scrollTrack.transform, 0, 0, 255, 4, Gold, 2);
        previousMap = Button(footer, "PreviousStages", 0, 0, 58, 42, "‹", () => Scroll(-mapWidth * .72f)).transform.parent.gameObject;
        nextMap = Button(footer, "NextStages", 0, 0, 58, 42, "›", () => Scroll(mapWidth * .72f)).transform.parent.gameObject;
        Resize();
        ShowChapter(chapter, focus);
    }

    public override void DoHide()
    {
        if (GonnaHide) return;
        base.DoHide();
        gameObject.SetActive(false);
        transform.SetParent(null, false);
        Destroy(gameObject);
    }

    public override bool ProcessEsc()
    {
        if (expanded) CollapseDrawer();
        else DoHide();
        return true;
    }

    private void LateUpdate() => Resize();

    private void Resize()
    {
        if (uiCamera == null || GonnaHide) return;
        if (uiRoot != null) LocalUiLayoutRuntime.PrepareRoot(uiRoot);
        int revision = uiRoot != null && LocalUiLayoutRuntime.Enabled ? LocalUiLayoutRuntime.Metrics(uiRoot).Revision : 0;
        var cameraPixels = uiCamera.pixelRect;
        var safe = Screen.safeArea;
        var pixels = Rect.MinMaxRect(Mathf.Max(cameraPixels.xMin, safe.xMin), Mathf.Max(cameraPixels.yMin, safe.yMin),
            Mathf.Min(cameraPixels.xMax, safe.xMax), Mathf.Min(cameraPixels.yMax, safe.yMax));
        if (pixels.width < 1 || pixels.height < 1) pixels = cameraPixels;
        if (pixels == lastSafePixels && cameraPixels == lastCameraPixels && revision == canvasRevision) return;
        lastSafePixels = pixels;
        lastCameraPixels = cameraPixels;
        canvasRevision = revision;
        Rect screen = ProjectPixels(cameraPixels);
        Rect safeView = ProjectPixels(pixels);
        float scale = Mathf.Max(.05f, Mathf.Min(safeView.width / 920f, safeView.height / 620f));
        layout.localScale = Vector3.one * scale;
        screen = new Rect(screen.position / scale, screen.size / scale);
        safeView = new Rect(safeView.position / scale, safeView.size / scale);
        float top = safeView.yMax - HeaderHeight;
        float bottom = safeView.yMin + FooterHeight;
        Rect view = Rect.MinMaxRect(screen.xMin, bottom, screen.xMax, top);

        SetSurface(landscape, screen);
        SetCollider(pageHit, screen);
        SetSurface(headerSurface, Rect.MinMaxRect(screen.xMin, top, screen.xMax, screen.yMax));
        SetSurface(footerSurface, Rect.MinMaxRect(screen.xMin, screen.yMin, screen.xMax, bottom));
        header.localPosition = new Vector3(safeView.center.x, top + HeaderHeight * .5f, 0);
        footer.localPosition = new Vector3(safeView.center.x, bottom - FooterHeight * .5f, 0);
        backButton.localPosition = new Vector3(safeView.xMin - safeView.center.x + 41, 0, 0);
        drawerButton.transform.parent.localPosition = new Vector3(safeView.xMin - safeView.center.x + 151, 0, 0);
        previousMap.transform.localPosition = new Vector3(safeView.xMin - safeView.center.x + 40, 5, 0);
        nextMap.transform.localPosition = new Vector3(safeView.xMax - safeView.center.x - 40, 5, 0);

        float left = Mathf.Max(0, safeView.xMin - screen.xMin);
        float right = Mathf.Max(0, screen.xMax - safeView.xMax);
        float oldCenter = RouteCenter(mapWidth);
        map.localPosition = new Vector3(view.center.x, view.center.y, -25);
        mapWidth = view.width;
        mapHeight = view.height;
        map.GetComponent<UIPanel>().clipRange = new Vector4(0, 0, mapWidth, mapHeight);
        panHit.size = new Vector3(mapWidth, mapHeight, .25f);
        stageInsets = new Vector2(left, right);
        if (current != null)
        {
            UpdateRouteBounds();
            Scroll(oldCenter - RouteCenter(mapWidth));
        }
        ResizeDrawer();
    }

    private Rect ProjectPixels(Rect pixels)
    {
        float distance = uiCamera.WorldToScreenPoint(transform.position).z;
        var lower = transform.InverseTransformPoint(uiCamera.ScreenToWorldPoint(new Vector3(pixels.xMin, pixels.yMin, distance)));
        var upper = transform.InverseTransformPoint(uiCamera.ScreenToWorldPoint(new Vector3(pixels.xMax, pixels.yMax, distance)));
        return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
    }

    private static void SetSurface(UITexture widget, Rect rect)
    {
        var position = widget.transform.localPosition;
        widget.transform.localPosition = new Vector3(rect.center.x, rect.center.y, position.z);
        widget.transform.localScale = new Vector3(rect.width, rect.height, 1);
    }

    private static void SetCollider(BoxCollider collider, Rect rect)
    {
        collider.center = rect.center;
        collider.size = new Vector3(rect.width, rect.height, .25f);
    }

    private void ResizeDrawer()
    {
        if (drawer == null || !expanded) return;
        var bounds = new Rect(-mapWidth * .5f, -mapHeight * .5f, mapWidth, mapHeight);
        drawer.localPosition = new Vector3(map.localPosition.x, map.localPosition.y, -90);
        SetSurface(drawerShade, bounds);
        SetCollider(drawerHit, bounds);
        sidebar.localPosition = new Vector3(bounds.xMin + stageInsets.x * .5f + DrawerWidth * .5f, bounds.yMax, -5);
        float width = DrawerWidth + stageInsets.x;
        SetSurface(drawerEdge, new Rect(-width * .5f, -mapHeight, width, mapHeight));
        SetSurface(drawerSurface, new Rect(-width * .5f, -mapHeight, width - 1, mapHeight));
        SetCollider(sidebarHit, new Rect(-width * .5f, -mapHeight, width, mapHeight));
        chapterEntries.localPosition = new Vector3(stageInsets.x * .5f, 0, 0);
        chapterPaging.localPosition = new Vector3(stageInsets.x * .5f, -mapHeight + 29, 0);
    }

    private void SetChapter(int firstId, int focus) =>
        ShowChapter(chapters.First(row => row.Stages[0].Id == firstId), focus);

    private void ShowChapter(Chapter chapter, int focus)
    {
        current = chapter;
        string mapArt = chapter.Stages[0].MapArt;
        landscape.mainTexture = (string.IsNullOrEmpty(mapArt) ? null : Resources.Load<Texture2D>("LocalStoryArt/" + mapArt)) ?? defaultLandscape;
        expanded = false;
        UpdateDrawer();
        heading.text = chapter.Title;
        completion.text = "已通关 " + chapter.Cleared + " / " + chapter.Stages.Length;
        var oldRoute = route;
        oldRoute.gameObject.SetActive(false);
        Destroy(oldRoute.gameObject);
        route = Child(map, "Route", 0, 0, -5);

        var stages = chapter.Stages;
        var points = new Dictionary<int, Vector2>();
        routeWidth = (stages.Length - 1) * StageSpacing + 2 * RouteMargin;
        for (int i = 0; i < stages.Length; i++)
            points.Add(stages[i].Id, new Vector2(RouteMargin + i * StageSpacing, stages.Length == 1 ? 35 : RouteHeights[i % RouteHeights.Length]));
        for (int i = 1; i < stages.Length; i++)
        {
            var stage = stages[i];
            if (points.TryGetValue(stage.PreviousStage, out var previous))
                DrawTrail(previous, points[stage.Id], LocalStoryMenu.Progress(stage.Id)?.unlocked == true);
        }

        for (int i = 0; i < stages.Length; i++)
            DrawStage(stages[i], i + 1, points[stages[i].Id]);

        if (!points.ContainsKey(focus))
            focus = stages.FirstOrDefault(stage => LocalStoryMenu.Progress(stage.Id)?.unlocked == true &&
                LocalStoryMenu.Progress(stage.Id)?.stars == 0)?.Id ?? stages[0].Id;
        FocusStage = focus;
        offset = 0;
        UpdateRouteBounds();
        Scroll(points[focus].x - (mapWidth - stageInsets.x - stageInsets.y) * .5f);
    }

    private float RouteCenter(float width) => (width - stageInsets.x - stageInsets.y) * .5f;

    private void UpdateRouteBounds() => limit = Mathf.Max(0, routeWidth - 2 * RouteCenter(mapWidth));

    private void DrawTrail(Vector2 from, Vector2 to, bool open)
    {
        Color color = open ? Gold : Dim;
        int count = Mathf.CeilToInt((to.x - from.x) / 16f);
        for (int i = 1; i < count; i++)
        {
            float t = (float)i / count;
            float x = Mathf.Lerp(from.x, to.x, t), y = Mathf.Lerp(from.y, to.y, Mathf.SmoothStep(0, 1, t));
            RectWidget(route, x, y, 10, 6, Ink, 1);
            RectWidget(route, x, y, 7, 3, color, 2);
        }
    }

    private void DrawStage(LocalStoryChapter stage, int number, Vector2 point)
    {
        var state = LocalStoryMenu.Progress(stage.Id);
        bool open = state?.unlocked == true;
        var node = Child(route, "StoryStage" + stage.Id, point.x, point.y, -2);
        var hit = PanSurface(node, "StageHit", 0, -23, 190, 128, -4);
        hit.onClick = _ =>
        {
            if (GonnaHide) return;
            if (!open)
            {
                CollapseDrawer();
                NoticeDialog.DoModal(LocalStoryMenu.LockReason(stage), null, true, false, stage.Title, true);
                return;
            }
            LocalStoryMenu.OpenChapter(stage);
        };
        var pin = Child(node, "StagePin", 0, 0);
        if (stage.Boss)
            Sprite(pin, bossAtlas, open ? "ChallengeTower_Indicator_Boss" : "ChallengeTower_Indicator_Boss_Disable", 0, 0, 72, 72, 4);
        else
        {
            RectWidget(pin, 0, 0, 48, 48, Ink, 3).transform.localRotation = Quaternion.Euler(0, 0, 45);
            RectWidget(pin, 0, 0, 42, 42, open ? Gold : Dim, 4).transform.localRotation = Quaternion.Euler(0, 0, 45);
            RectWidget(pin, 0, 0, 37, 37, open ? PanelInk : Ink, 5).transform.localRotation = Quaternion.Euler(0, 0, 45);
            if (open) Label(pin, "StageNumber", 0, 0, 21, number.ToString("00"), Paper);
            else Sprite(pin, atlas, "UI_Icon_lock", 0, 1, 22, 22, 7);
        }
        var plate = Child(node, "StageCaption", 0, -63);
        RectWidget(plate, 0, 0, 192, 48, open ? Gold : Dim, 3);
        RectWidget(plate, 0, 0, 190, 46, Ink, 4);
        Label(plate, "StageTitle", 0, 10, 19, stage.Title, open ? Paper : Dim, 183);
        string status = state?.stars > 0 ? state.stars + " 星" : open ? "可挑战" : "未解锁";
        if (state?.canSweep == true) status = "扫荡 " + state.sweepsLeft + "/" + stage.DailySweeps + " · " + status;
        Label(plate, "StageStatus", 0, -13, 15, (stage.Boss ? "魔王 · " : "") + status, open ? Jade : Dim, 183);
    }

    private UIEventListener PanSurface(Transform parent, string name, float x, float y, float width, float height, float z)
    {
        var input = InputSurface(parent, name, x, y, width, height, z);
        input.onDrag = (_, delta) =>
        {
            if (GonnaHide || uiCamera == null) return;
            var zero = uiCamera.WorldToScreenPoint(layout.position);
            var shifted = uiCamera.WorldToScreenPoint(layout.TransformPoint(Vector3.right));
            Scroll(-delta.x / Mathf.Max(.01f, Mathf.Abs(shifted.x - zero.x)));
        };
        input.onScroll = (_, delta) => Scroll(-delta * StageSpacing);
        return input;
    }

    private void Scroll(float change)
    {
        if (GonnaHide) return;
        offset = Mathf.Clamp(offset + change, 0, limit);
        var position = route.localPosition;
        float visibleWidth = 2 * RouteCenter(mapWidth);
        position.x = -mapWidth * .5f + stageInsets.x - offset + Mathf.Max(0, visibleWidth - routeWidth) * .5f;
        route.localPosition = position;
        float fraction = limit > 0 ? offset / limit : 0;
        // Cover at the original atlas ratio and pan slowly behind the route.
        var shape = landscape.transform.localScale;
        float ratio = (landscape.mainTexture.width / (float)landscape.mainTexture.height) * shape.y / shape.x;
        float zoom = limit > 0 ? .77f : 1;
        float uvWidth = zoom * Mathf.Min(1, 1 / ratio);
        float uvHeight = uvWidth * ratio;
        float pan = limit > 0 ? fraction : .5f;
        var uv = new Rect((1 - uvWidth) * pan, (1 - uvHeight) * .5f, uvWidth, uvHeight);
        landscape.uvRect = uv;
        previousMap.SetActive(offset > .01f);
        nextMap.SetActive(offset < limit - .01f);
        scrollTrack.SetActive(limit > 0);
        footerSurface.gameObject.SetActive(limit > 0);
        float scrollWidth = Mathf.Min(255, 255 * visibleWidth / routeWidth);
        scrollThumb.transform.localScale = new Vector3(scrollWidth, 4, 1);
        scrollThumb.transform.localPosition = new Vector3((255 - scrollWidth) * (fraction - .5f), 0, 0);
        int first = Mathf.Clamp(Mathf.CeilToInt((offset - RouteMargin) / StageSpacing) + 1, 1, current.Stages.Length);
        int last = Mathf.Clamp(Mathf.FloorToInt((offset + visibleWidth - RouteMargin) / StageSpacing) + 1, first, current.Stages.Length);
        range.text = limit > 0 ? "第 " + first + "–" + last + " 关   ·   左右拖动" : "";
    }

    private void ToggleDrawer()
    {
        if (GonnaHide) return;
        expanded = !expanded;
        drawerLine = Array.IndexOf(Lines, current.Line);
        listPage = Array.IndexOf(chapters.Where(row => row.Line == current.Line).ToArray(), current) / ChaptersPerPage;
        UpdateDrawer();
    }

    private void CollapseDrawer()
    {
        if (!expanded) return;
        expanded = false;
        UpdateDrawer();
    }

    private void UpdateDrawer()
    {
        if (drawer != null)
        {
            drawer.gameObject.SetActive(false);
            Destroy(drawer.gameObject);
        }
        drawerButton.text = expanded ? "章节  ▾" : "章节  ▸";
        if (!expanded) return;
        drawer = Panel(layout, "ChapterDrawer", 0, 0, -90);
        var closeDrawer = InputSurface(drawer, "DismissDrawer", 0, 0, 1, 1, 0);
        closeDrawer.onClick = _ => CollapseDrawer();
        drawerHit = closeDrawer.GetComponent<BoxCollider>();
        drawerShade = RectWidget(drawer, 0, 0, 1, 1, new Color(0, 0, 0, .27f), 0);
        sidebar = Panel(drawer, "ChapterList", 0, 0, -5);
        sidebarHit = InputSurface(sidebar, "DrawerSurface", 0, 0, 1, 1, 0).GetComponent<BoxCollider>();
        drawerEdge = RectWidget(sidebar, 0, 0, 1, 1, Gold, 0);
        drawerSurface = RectWidget(sidebar, 0, 0, 1, 1, Ink, 1);
        chapterEntries = Child(sidebar, "ChapterEntries");
        chapterPaging = Child(sidebar, "ChapterPaging");
        for (int i = 0; i < Lines.Length; i++)
        {
            int tab = i;
            Button(chapterEntries, "StoryLine" + Lines[i], (i - 1) * 120, -39, 111, 44, LineNames[i], () =>
            {
                drawerLine = tab;
                listPage = 0;
                UpdateDrawer();
            }, tab == drawerLine);
        }
        var rows = chapters.Where(row => row.Line == Lines[drawerLine]).ToArray();
        int pages = Mathf.CeilToInt((float)rows.Length / ChaptersPerPage);
        int iRow = 0;
        foreach (var row in rows.Skip(listPage * ChaptersPerPage).Take(ChaptersPerPage))
        {
            var title = Button(chapterEntries, "StoryChapter" + row.Stages[0].Id, 0, -109 - iRow++ * 65, 346, 58,
                row.Title + "\n已通关 " + row.Cleared + " / " + row.Stages.Length,
                () => ShowChapter(row, 0), row == current);
            title.multiLine = true;
        }
        if (pages > 1)
        {
            if (listPage > 0) Button(chapterPaging, "PreviousChapters", -145, 0, 54, 38, "‹", () => { listPage--; UpdateDrawer(); });
            Label(chapterPaging, "ChapterPage", 0, 0, 17, (listPage + 1) + " / " + pages, Paper);
            if (listPage < pages - 1) Button(chapterPaging, "NextChapters", 145, 0, 54, 38, "›", () => { listPage++; UpdateDrawer(); });
        }
        ResizeDrawer();
    }

    private UILabel Button(Transform parent, string name, float x, float y, float width, float height,
        string text, Action onClick, bool selected = false)
    {
        var button = Child(parent, name, x, y);
        RectWidget(button, 0, 0, width, height, selected ? Gold : Dim, 1);
        RectWidget(button, 0, 0, width - 2, height - 2, selected ? new Color32(72, 76, 55, 255) : PanelInk, 2);
        var label = Label(button, "Label", 0, 0, 19, text, Paper, (int)width - 8);
        InputSurface(button, "Hit", 0, 0, width, height, -2).onClick = _ => { if (!GonnaHide) onClick(); };
        return label;
    }

    private static UIEventListener InputSurface(Transform parent, string name, float x, float y, float width, float height, float z)
    {
        var input = Child(parent, name, x, y, z).gameObject;
        var hit = input.AddComponent<BoxCollider>();
        hit.size = new Vector3(width, height, .25f);
        return UIEventListener.Get(input);
    }

    private static Transform Child(Transform parent, string name, float x = 0, float y = 0, float z = 0)
    {
        var node = NGUITools.AddChild(parent.gameObject);
        node.name = name;
        node.transform.localPosition = new Vector3(x, y, z);
        return node.transform;
    }

    private static Transform Panel(Transform parent, string name, float x, float y, float z)
    {
        var node = Child(parent, name, x, y, z);
        node.gameObject.AddComponent<UIPanel>();
        return node;
    }

    private UITexture RectWidget(Transform parent, float x, float y, float width, float height, Color color, int depth, Texture texture = null)
    {
        var node = Child(parent, "Surface", x, y);
        node.localScale = new Vector3(width, height, 1);
        var widget = node.gameObject.AddComponent<UITexture>();
        widget.pivot = UIWidget.Pivot.Center;
        if (texture == null) widget.material = solid;
        else
        {
            widget.shader = solid.shader;
            widget.mainTexture = texture;
        }
        widget.depth = depth;
        widget.color = color;
        return widget;
    }

    private UILabel Label(Transform parent, string name, float x, float y, float size, string text, Color color, int width = 0)
    {
        var node = Child(parent, name, x, y, -1);
        node.localScale = new Vector3(size, size, 1);
        var label = node.gameObject.AddComponent<UILabel>();
        label.font = font;
        label.pivot = UIWidget.Pivot.Center;
        label.depth = 8;
        label.lineWidth = width;
        label.color = color;
        label.text = text;
        return label;
    }

    private static void Sprite(Transform parent, UIAtlas atlas, string name, float x, float y, float width, float height, int depth)
    {
        if (atlas == null) return;
        var node = Child(parent, name, x, y, -1);
        var sprite = node.gameObject.AddComponent<UISprite>();
        sprite.atlas = atlas;
        sprite.spriteName = name;
        sprite.depth = depth;
        node.localScale = new Vector3(width, height, 1);
    }

    private void OnDestroy()
    {
        if (solid != null) Destroy(solid);
    }
}
