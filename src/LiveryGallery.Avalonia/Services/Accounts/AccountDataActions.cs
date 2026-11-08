using LiveryGallery.ViewModels;

namespace LiveryGallery.Services.Accounts;

/// <summary>What the settings dialog may do with the accounts and their stored data, without knowing the services behind it</summary>
/// <param name="List">Every account with data, and how much it takes (the data page)</param>
/// <param name="Forget">Deletes an account's data</param>
/// <param name="Accounts">The accounts the switcher offers (the folders page); null hides the list</param>
/// <param name="Add">Adds an account by its save folder</param>
internal sealed record AccountDataActions(
    Func<Task<List<StoredAccount>>> List, Func<string, Task<bool>> Forget,
    Func<Task<List<AccountListItem>>>? Accounts = null, Func<string, Task<AccountAddResult>>? Add = null);
