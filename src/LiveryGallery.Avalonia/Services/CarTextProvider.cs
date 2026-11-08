using Microsoft.Extensions.Logging;
using ForzaToolkit.StringTables;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

/// <summary>A car's texts as a livery card shows them</summary>
/// <param name="Texts">Make, model, country and car type in the language in use</param>
/// <param name="CountryRaw">The country from the car database (English): quick filters match it</param>
/// <param name="CarTypeRaw">The car type from the car database (English): quick filters match it</param>
/// <param name="UnavailableNote">Why the texts are not in the chosen language (no game folder…); null when they are</param>
internal sealed record CarTextResult(CarTexts Texts, string CountryRaw, string CarTypeRaw, string? UnavailableNote);

/// <summary>
/// The texts of a car as the gallery shows them: make, model, country and car type in the chosen game language
/// (the setting, or the game language of the interface language). The texts come from the game's own string tables,
/// kept by <see cref="CarLocalizationService"/> next to the car list in the ForzaToolkit cache; without the game folder
/// (or its language files), from the texts published with the car list (<see cref="CarDatabaseService.Texts"/>:
/// <c>strings.json</c>, <c>car-names.json</c>). Only when neither has the language are the car list's own (English)
/// texts shown, with a note why. Thread-safe.
/// </summary>
internal sealed class CarTextProvider(CarDatabaseService cars, CarLocalizationService localization, AppSettingsData settings)
{
    // Which texts a car gets: ForzaToolkit.CarDatabase, the same as in the converter
    private readonly CarTextSelector _texts = new(cars, localization, () => settings.CarTextLanguage, () => AppLocalisationService.Culture,
        AppLog.LoggerFactory.CreateLogger("CarTexts"));

    public CarLocalizationService Localization => localization;

    public GameTextsState State => _texts.State;

    /// <summary>The game language the user wants: the setting, or the one of the interface</summary>
    public string WantedLanguage => _texts.WantedLanguage;

    /// <summary>The language the texts are shown in (<see cref="CarTextSelector.LanguageCode"/>)</summary>
    public string LanguageCode => _texts.LanguageCode;

    /// <summary>The game languages there are car texts in (<see cref="CarTextSelector.Languages"/>)</summary>
    public IReadOnlyList<GameLanguage> Languages => _texts.Languages;

    /// <summary>Why the texts are not in the wanted language; null when they are (or English is wanted)</summary>
    public string? UnavailableNote => _texts.MissingLanguageReason switch
    {
        GameTextsState.NoGameFolder => Strings.CarTextsNoGameFolder,
        GameTextsState.NoStringTables => Strings.CarTextsNoStringTables,
        _ => null,
    };

    /// <summary>The texts of the car with <paramref name="carId"/>; null when the car list does not know it</summary>
    public CarTextResult? For(int carId) =>
        cars.Get(carId) is { } car ? new CarTextResult(_texts.Localize(car), car.Country ?? "", car.CarType ?? "", UnavailableNote) : null;

    /// <summary>
    /// Looks at the game folder (the settings', or the one found on this PC) and reads its language archives that are new
    /// or changed (<see cref="CarTextSelector.UpdateFromGameAsync"/>). True when what is shown changes. Never throws except
    /// for cancellation.
    /// </summary>
    public async Task<bool> UpdateFromGameAsync(GameProfile profile, CancellationToken ct)
    {
        string? path = string.IsNullOrWhiteSpace(settings.GameInstallPath)
            ? await profile.FindInstallFolderAsync()
            : settings.GameInstallPath;
        return await _texts.UpdateFromGameAsync(path, ct);
    }
}
