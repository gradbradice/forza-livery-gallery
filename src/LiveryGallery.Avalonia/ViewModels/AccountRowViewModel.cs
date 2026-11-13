using LiveryGallery.Localisation;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

/// <summary>An account in the settings' list of accounts: its name, the end of its XUID and its save folder</summary>
internal sealed class AccountRowViewModel(AccountListItem item)
{
    public AccountListItem Item { get; } = item;

    /// <summary>The player's name, or "Account" while it is not known</summary>
    public string Name => AccountLabels.Name(Item.Info.Name);

    /// <summary>The name in one line, for the questions about this account ("Account ***E1FB" when unnamed)</summary>
    public string Title => AccountLabels.Title(Item.Info.Key, Item.Info.Name);

    /// <summary>The end of the XUID beside the name: "***E1FB", never the whole XUID</summary>
    public string Marker => AccountLabels.Marker(Item.Info.Key);

    public string SavePath => Item.Info.SavePath;

    public string DetailText
    {
        get
        {
            string origin = Item.IsFound ? Strings.SettingsAccountFound : Strings.SettingsAccountAdded;
            return Item.Info.ItemCount is { } count ? origin + " · " + string.Format(Strings.AccountLiveriesFormat, count) : origin;
        }
    }

    public bool IsCurrent => Item.IsCurrent;

    public string CurrentText => Strings.AccountDataCurrent;

    /// <summary>An account added by hand can be taken off the list; one found on this PC comes back at every search</summary>
    public bool CanForget => !Item.IsCurrent && !Item.IsFound;

    public string ForgetButtonText => Strings.AccountDataForgetButton;
}
