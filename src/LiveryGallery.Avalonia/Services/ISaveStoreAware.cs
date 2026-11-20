namespace LiveryGallery.Services;

/// <summary>
/// A livery server client that tells the server which build's files the save is (FH3 to FH5: the Steam and the
/// Microsoft Store builds keep and encrypt the profile differently). The window gives it the save shown
/// </summary>
internal interface ISaveStoreAware
{
    /// <summary>Which build's files the save shown is (<see cref="ProfileDataLocator.StoreKindOf"/>); null when it cannot be told</summary>
    Func<ForzaToolkit.Saves.SaveStoreKind?>? SaveStore { get; set; }
}
