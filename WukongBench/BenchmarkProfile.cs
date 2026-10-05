namespace WukongBench;

/// <summary>
/// Набор настроек одного прохода бенчмарка.
/// </summary>
/// <param name="UiSettings">Значения внутри UISettingData (то, что показывает меню игры).</param>
/// <param name="IniValues">Обычные ключи ini: (секция, ключ) → значение.</param>
/// <param name="ExpectedResolution">Разрешение «ширина × высота» для самопроверки результата.</param>
/// <param name="ExpectedQualityLevel">Ожидаемый уровень качества (меню) для самопроверки.</param>
/// <param name="Rationale">Обоснование выбора настроек (для отчёта).</param>
public sealed record BenchmarkProfile(
    string Name,
    IReadOnlyDictionary<string, string> UiSettings,
    IReadOnlyDictionary<(string Section, string Key), string> IniValues,
    string ExpectedResolution,
    int ExpectedQualityLevel,
    string Rationale)
{
    private const string MainSection = "/Script/GSGameSettings.GSGameUserSettings";
    private const string SgSection = "ScalabilityGroups";

    /// <summary>Ключи качества в UISettingData (значения меню: 1..5).</summary>
    private static readonly string[] UiQualityKeys =
    [
        "ViewDistance", "AntiAliasing", "PostProcessing", "ShadowQuality", "TextureQuality",
        "FxQuality", "MaterialQuality", "VegetationQuality", "GlobalIllumination", "ReflectionQuality",
    ];

    /// <summary>Ключи качества в [ScalabilityGroups] (значения групп Unreal: 0..4, где 0 — «кино»).</summary>
    private static readonly string[] SgQualityKeys =
    [
        "sg.ViewDistanceQuality", "sg.AntiAliasingQuality", "sg.ShadowQuality", "sg.GlobalIlluminationQuality",
        "sg.ReflectionQuality", "sg.PostProcessQuality", "sg.TextureQuality", "sg.EffectsQuality",
        "sg.FoliageQuality", "sg.ShadingQuality",
    ];

    /// <summary>CPU-тест: минимальная нагрузка на видеокарту, максимум — на процессор.</summary>
    public static BenchmarkProfile Cpu { get; } = Create(
        name: "CPU",
        width: 1280, height: 720, resolutionIndex: "2",
        renderHeight: "356", renderPercent: "50",
        uiQuality: 1,
        rationale:
            "Разрешение 1280×720 и внутренний масштаб рендеринга 50% (фактически ~640×360) сводят нагрузку " +
            "на GPU к минимуму: видеокарта перестаёт быть узким местом, а итоговый FPS упирается в процессор. " +
            "Все параметры качества (тени, постобработка, освещение и пр.) — на минимум, что дополнительно " +
            "снимает с GPU работу. Трассировка лучей, генерация кадров и VSync отключены, чтобы не искажать " +
            "показатели. В результате число кадров в секунду и доля кадров, где процессор считает кадр дольше " +
            "видеокарты, отражают именно производительность CPU.");

    /// <summary>GPU-тест: максимальная нагрузка на видеокарту.</summary>
    public static BenchmarkProfile Gpu { get; } = Create(
        name: "GPU",
        width: 1920, height: 1080, resolutionIndex: "0",
        renderHeight: "1080", renderPercent: "100",
        uiQuality: 5,
        rationale:
            "Разрешение 1920×1080 при полном (100%) внутреннем масштабе рендеринга и все параметры качества " +
            "на максимум создают для видеокарты предельную нагрузку: при высоком разрешении и полном наборе " +
            "эффектов (тени, глобальное освещение, отражения, постобработка) GPU почти всегда является узким " +
            "местом, поэтому средний FPS и время кадра показывают именно производительность видеокарты. " +
            "Трассировка лучей и генерация кадров отключены намеренно: RT сильно отличается между поколениями " +
            "видеокарт и искажает сопоставимость, а генерация кадров вставляет искусственные кадры и завышает " +
            "результат.");

    private static BenchmarkProfile Create(
        string name, int width, int height, string resolutionIndex,
        string renderHeight, string renderPercent, int uiQuality, string rationale)
    {
        var ui = new Dictionary<string, string>
        {
            ["ScreenMode"] = "1",               // полноэкранное окно
            ["ScreenResolution"] = resolutionIndex,
            ["ImageQuality"] = renderHeight,    // высота внутреннего рендера в пикселях
            ["WindowFullImageQuality"] = "0",
            ["SuperResolutionSampling"] = "3",  // апскейлер: оставляем допустимое значение из рабочего конфига
            ["Vsync"] = "0",                    // без вертикальной синхронизации
            ["LockFrameRate"] = "0",            // без ограничения FPS
            ["MotionBlur"] = "0",               // без размытия в движении
            ["InsertFrame"] = "0",              // без генерации кадров
            ["Rtx"] = "0",                      // без трассировки лучей
            ["QualityLevel"] = uiQuality.ToString(),
        };
        foreach (var key in UiQualityKeys) ui[key] = uiQuality.ToString();

        var ini = new Dictionary<(string, string), string>
        {
            [(MainSection, "FullscreenMode")] = "1",
            [(MainSection, "LastConfirmedFullscreenMode")] = "1",
            [(MainSection, "PreferredFullscreenMode")] = "1",
            [(MainSection, "ResolutionSizeX")] = width.ToString(),
            [(MainSection, "ResolutionSizeY")] = height.ToString(),
            [(MainSection, "LastUserConfirmedResolutionSizeX")] = width.ToString(),
            [(MainSection, "LastUserConfirmedResolutionSizeY")] = height.ToString(),
            [(MainSection, "bUseVSync")] = "False",
            [(MainSection, "FrameRateLimit")] = "0.000000",
            [(SgSection, "sg.ResolutionQuality")] = renderPercent,
        };
        foreach (var key in SgQualityKeys) ini[(SgSection, key)] = (uiQuality - 1).ToString();

        return new BenchmarkProfile(name, ui, ini, $"{width} × {height}", uiQuality, rationale);
    }
}