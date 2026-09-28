using System.Globalization;
using Accession.Core.Inventories;
using Accession.Core.Settings;
using Accession.Presentation.Mvvm;
using Accession.Presentation.ViewModels;
using Accession.UI.Components;
using Accession.UI.FilesScreen;
using Accession.UI.Forms;

namespace Accession.Presentation.WebForms;

/// <summary>Describes the dialog view models as web forms. The view models keep their logic and validation.</summary>
public static class DialogForms
{
    /// <summary>A form for <paramref name="viewModel"/>, or null if it has no web form.</summary>
    public static FormDialog? Build(DialogViewModelBase viewModel, Action dismiss) => viewModel switch
    {
        NewInventoryViewModel vm => NewInventory(vm, dismiss),
        InventoryPropertiesViewModel vm => Properties(vm, dismiss),
        ChangeRootPathViewModel vm => ChangeRootPath(vm, dismiss),
        AddMediaViewModel vm => AddMedia(vm, dismiss),
        DeleteMediaViewModel vm => DeleteMedia(vm, dismiss),
        SettingsViewModel vm => Settings(vm, dismiss),
        ErrorDialogViewModel vm => Error(vm, dismiss),
        ExportViewModel vm => Export(vm, dismiss),
        SavedSearchViewModel vm => SavedSearch(vm, dismiss),
        _ => null,
    };

    private static FormDialog NewInventory(NewInventoryViewModel vm, Action dismiss) => new(
        "New inventory",
        [
            new FormSection("Matter",
            [
                new TextField("Client name", () => vm.ClientName, v => vm.ClientName = v) { Error = () => vm.Errors[InventoryFields.ClientName], AutoFocus = true },
                new TextField("Client ID", () => vm.ClientCode, v => vm.ClientCode = v) { Error = () => vm.Errors[InventoryFields.ClientCode] },
                new TextField("Matter name", () => vm.MatterName, v => vm.MatterName = v) { Error = () => vm.Errors[InventoryFields.MatterName] },
                new TextField("Matter ID", () => vm.MatterCode, v => vm.MatterCode = v) { Error = () => vm.Errors[InventoryFields.MatterCode] },
                new TextField("Description", () => vm.Description, v => vm.Description = v) { Multiline = true, Hint = "Optional." },
                new TextField("Matter link", () => vm.MatterUrl, v => vm.MatterUrl = v)
                {
                    Wide = true, Placeholder = "https://…", Hint = "Optional link to the matter in your practice management system.",
                    Error = () => vm.Errors[InventoryFields.MatterUrl],
                },
            ]),
            new FormSection("Media and inventory file",
            [
                new TextField("Root folder", () => vm.RootFolder, v => vm.RootFolder = v)
                {
                    Mono = true, Browse = vm.BrowseRootFolderCommand, Placeholder = @"\\server\share\matter",
                    Hint = "The folder that contains one subfolder per media, named by Media ID.",
                    Error = () => vm.Errors[InventoryFields.RootFolder],
                },
                new NoteItem(() => vm.RootInfo) { Tone = "info" },
                new TextField("Save inventory as", () => vm.SavePath, v => vm.SavePath = v)
                {
                    Mono = true, Browse = vm.BrowseSavePathCommand, Error = () => vm.Errors[InventoryFields.SavePath],
                },
                new CheckField("Show the media found under the root after creating", () => vm.ShowDiscoveredMedia, v => vm.ShowDiscoveredMedia = v),
            ]),
        ],
        [
            new FormButton("Cancel", vm.CancelCommand),
            new FormButton("Create inventory", vm.CreateCommand, DialogChoiceStyle.Primary) { IsDefault = true },
        ])
    {
        Width = "680px",
        Observed = [vm, vm.Errors],
        Dismiss = dismiss,
    };

    private static FormDialog Properties(InventoryPropertiesViewModel vm, Action dismiss)
    {
        Action<string>? Editable(Action<string> set) => vm.CanEdit ? set : null;
        return new FormDialog(
            "Inventory properties",
            [
                new FormSection("Matter",
                [
                    new TextField("Client name", () => vm.ClientName, Editable(v => vm.ClientName = v)) { Error = () => vm.Errors[InventoryFields.ClientName] },
                    new TextField("Client ID", () => vm.ClientCode, Editable(v => vm.ClientCode = v)) { Error = () => vm.Errors[InventoryFields.ClientCode] },
                    new TextField("Matter name", () => vm.MatterName, Editable(v => vm.MatterName = v)) { Error = () => vm.Errors[InventoryFields.MatterName] },
                    new TextField("Matter ID", () => vm.MatterCode, Editable(v => vm.MatterCode = v)) { Error = () => vm.Errors[InventoryFields.MatterCode] },
                    new TextField("Description", () => vm.Description, Editable(v => vm.Description = v)) { Multiline = true },
                    new TextField("Matter link", () => vm.MatterUrl, Editable(v => vm.MatterUrl = v)) { Wide = true, Error = () => vm.Errors[InventoryFields.MatterUrl] },
                    new NoteItem(() => vm.IsReadOnly ? "The inventory is open read-only, so these fields cannot be changed." : string.Empty) { Tone = "info" },
                ]),
                new FormSection("Inventory",
                [
                    new InfoField("Root folder", () => vm.RootPath) { Mono = true, Wide = true },
                    new InfoField("Root status", () => vm.RootStatus),
                    new InfoField("Lock", () => vm.LockHolder),
                    new InfoField("Inventory file", () => vm.DbPath) { Mono = true, Wide = true },
                    new InfoField("Created", () => $"{vm.CreatedAt} by {vm.CreatedBy}"),
                    new InfoField("Created with", () => "Accession " + vm.CreatedAppVersion),
                    new InfoField("Schema version", () => vm.SchemaVersion),
                    new InfoField("Inventory ID", () => vm.InventoryGuid) { Mono = true },
                ]),
            ],
            [
                new FormButton("Change root path…", vm.ChangeRootPathCommand) { IsSecondary = true },
                new FormButton(vm.CanEdit ? "Cancel" : "Close", vm.CancelCommand),
                .. vm.CanEdit ? new[] { new FormButton("Save", vm.SaveCommand, DialogChoiceStyle.Primary) { IsDefault = true } } : [],
            ])
        {
            Width = "680px",
            Observed = [vm, vm.Errors],
            Dismiss = dismiss,
        };
    }

    private static FormDialog ChangeRootPath(ChangeRootPathViewModel vm, Action dismiss) => new(
        "Change root path",
        [
            new FormSection(null,
            [
                new NoteItem(() => "Use this when the media folders were moved, for example to another server. File records are kept; paths are relative to the root."),
                new InfoField("Current root", () => vm.CurrentRootPath) { Mono = true, Wide = true },
                new TextField("New root", () => vm.NewRootPath, v => vm.NewRootPath = v) { Mono = true, Browse = vm.BrowseCommand, AutoFocus = true },
                new NoteItem(() => vm.PreviewText) { Tone = "info" },
                new LinesItem("Media not found under the new root", () => vm.MediaNotFound),
            ]),
        ],
        [
            new FormButton("Cancel", vm.CancelCommand),
            new FormButton("Change root path", vm.ApplyCommand, DialogChoiceStyle.Primary) { IsDefault = true },
        ])
    {
        Width = "620px",
        Observed = [vm],
        Dismiss = dismiss,
    };

    private static FormDialog AddMedia(AddMediaViewModel vm, Action dismiss) => new(
        vm.IsNewMediaFound ? "New media found" : "Add media",
        [
            new FormSection(null,
            [
                new NoteItem(() => vm.IsNewMediaFound
                    ? "These folders under the root are not in the inventory yet. Tick the ones to add."
                    : "Tick the media folders to add. Each must be directly inside the root folder."),
                new InfoField("Root folder", () => vm.RootPath) { Mono = true, Wide = true },
                new ChecklistField("Media folders", () => [.. vm.Candidates.Select(c => new ChecklistEntry(c.MediaId, c.FullPath, () => c.IsChecked, v => c.IsChecked = v, c)
                    {
                        Badge = c.PreviouslyDeleted ? "deleted before" : null,
                    })])
                {
                    Add = vm.BrowseCommand, AddLabel = "Add another folder…", EmptyText = "No folders yet. Use “Add another folder…”.",
                },
                new CheckField("Start scanning the added media", () => vm.StartScanning, v => vm.StartScanning = v)
                {
                    Enabled = () => vm.CanScan,
                    Hint = vm.CanScan ? null : "Scanning is not available (read-only inventory or root offline).",
                },
            ]),
        ],
        [
            new FormButton(vm.CancelText, vm.CancelCommand),
            new FormButton("Add media", vm.AddCommand, DialogChoiceStyle.Primary) { IsDefault = true },
        ])
    {
        Width = "620px",
        Observed = [vm, vm.Candidates],
        Dismiss = dismiss,
    };

    private static FormDialog DeleteMedia(DeleteMediaViewModel vm, Action dismiss) => new(
        "Delete media",
        [
            new FormSection(null,
            [
                new NoteItem(() => $"Delete {vm.MediaId} from this inventory?\n\n{vm.Impact} The media folder itself is not touched. The deletion is recorded in the audit log.")
                {
                    Tone = "danger",
                },
                new TextField($"Type {vm.MediaId} to confirm", () => vm.ConfirmText, v => vm.ConfirmText = v) { Mono = true, AutoFocus = true, Wide = true },
            ]),
        ],
        [
            new FormButton("Cancel", vm.CancelCommand),
            new FormButton("Delete media", vm.DeleteCommand, DialogChoiceStyle.Danger) { IsDefault = true },
        ])
    {
        Width = "520px",
        Observed = [vm],
        Dismiss = dismiss,
    };

    private static FormDialog Settings(SettingsViewModel vm, Action dismiss)
    {
        string Errors(string property) => string.Join(" ", vm.GetErrors(property).Select(e => e.ErrorMessage));
        return new FormDialog(
            "Settings",
            [
                new FormSection("Display",
                [
                    new SelectField("Sizes", [.. vm.SizeUnitOptions.Select(o => new SelectOption(o.Value.ToString(), o.Label))],
                        () => vm.SizeUnit.ToString(), v => vm.SizeUnit = Enum.Parse<SizeUnitSystem>(v)),
                    new SelectField("Times", [.. vm.TimeZoneOptions.Select(o => new SelectOption(o.Value.ToString(), o.Label))],
                        () => vm.DisplayTimeZone.ToString(), v => vm.DisplayTimeZone = Enum.Parse<DisplayTimeZone>(v)),
                ]),
                new FormSection("Scanning",
                [
                    new NumberField("Listing threads", () => vm.EnumerationThreads, v => vm.EnumerationThreads = v,
                        SettingsLimits.MinEnumerationThreads, SettingsLimits.MaxEnumerationThreads)
                    {
                        Hint = vm.EnumerationThreadsRange, Error = () => Errors(nameof(vm.EnumerationThreads)),
                    },
                    new NumberField("Hashing threads", () => vm.HashingThreads, v => vm.HashingThreads = v,
                        SettingsLimits.MinHashingThreads, SettingsLimits.MaxHashingThreads)
                    {
                        Hint = vm.HashingThreadsRange, Error = () => Errors(nameof(vm.HashingThreads)),
                    },
                    new NumberField("Database batch size", () => vm.DbBatchSize, v => vm.DbBatchSize = v,
                        SettingsLimits.MinDbBatchSize, SettingsLimits.MaxDbBatchSize)
                    {
                        Step = 1_000, Hint = vm.DbBatchSizeRange + " rows per commit", Error = () => Errors(nameof(vm.DbBatchSize)),
                    },
                ]),
                new FormSection("Export",
                [
                    new TextField("Default export folder", () => vm.DefaultExportFolder, v => vm.DefaultExportFolder = v)
                    {
                        Mono = true, Browse = vm.BrowseExportFolderCommand, Placeholder = vm.DocumentsFolder,
                        Hint = "Empty uses your Documents folder.",
                    },
                    new CheckField("Split exports into one workbook per media", () => vm.SplitExportPerMedia, v => vm.SplitExportPerMedia = v),
                ]),
            ],
            [
                new FormButton("Restore defaults", vm.RestoreDefaultsCommand) { IsSecondary = true },
                new FormButton("Cancel", vm.CancelCommand),
                new FormButton("Save", vm.OkCommand, DialogChoiceStyle.Primary) { IsDefault = true },
            ])
        {
            Width = "620px",
            Observed = [vm],
            Dismiss = dismiss,
        };
    }

    private static FormDialog SavedSearch(SavedSearchViewModel vm, Action dismiss) => new(
        vm.Title,
        [
            new FormSection(null,
            [
                new TextField("Name", () => vm.Name, v => vm.Name = v) { AutoFocus = true, Wide = true, Error = () => vm.NameError },
                new TextField("Description", () => vm.Description, v => vm.Description = v)
                {
                    Multiline = true, Wide = true, Placeholder = "Optional: why these files are collected",
                },
            ]),
        ],
        [
            new FormButton("Cancel", vm.CancelCommand),
            new FormButton(vm.SaveText, vm.SaveCommand, DialogChoiceStyle.Primary) { IsDefault = true },
        ])
    {
        Width = "480px",
        Observed = [vm],
        Dismiss = dismiss,
    };

    /// <summary>Export to Excel (section 8.15).</summary>
    private static FormDialog Export(ExportViewModel vm, Action dismiss) => new(
        "Export to Excel",
        [
            new FormSection(null,
            [
                new SelectField("Scope", [.. vm.ScopeOptions.Select(o => new SelectOption(o.Value, o.Label))], () => vm.ScopeValue, v => vm.ScopeValue = v)
                {
                    Wide = true,
                },
                new ChecklistField("Media", () => [.. vm.Media.Select(m => new ChecklistEntry(m.MediaId, string.Empty, () => m.IsChecked, v => m.IsChecked = v, m))])
                {
                    Visible = () => vm.IsSelectedScope, Wide = true, EmptyText = "This inventory has no media.",
                },
                new InfoField("Files view", () => vm.FilesViewText.Length == 0 ? "All files" : vm.FilesViewText)
                {
                    Visible = () => vm.IsViewScope, Wide = true, Hint = "The Files sheet holds exactly the files the Files screen shows.",
                },
            ]),
            new FormSection("Sheets",
            [
                new CheckField("Summary", () => vm.SummarySheet, v => vm.SummarySheet = v) { Compact = true },
                new CheckField("Media", () => vm.MediaSheet, v => vm.MediaSheet = v) { Compact = true },
                new CheckField("Categories", () => vm.CategoriesSheet, v => vm.CategoriesSheet = v) { Compact = true },
                new CheckField("Extensions", () => vm.ExtensionsSheet, v => vm.ExtensionsSheet = v) { Compact = true },
                new CheckField("Files", () => vm.FilesSheet, v => vm.FilesSheet = v) { Compact = true },
                new CheckField("Errors", () => vm.ErrorsSheet, v => vm.ErrorsSheet = v) { Compact = true },
            ]),
            new FormSection("Output",
            [
                new InfoField("Units", () => vm.UnitsText) { Wide = true },
                new CheckField("One workbook per media", () => vm.OneWorkbookPerMedia, v => vm.OneWorkbookPerMedia = v)
                {
                    Wide = true, Hint = "Each workbook's name ends with its Media ID.",
                },
                new TextField("Workbook", () => vm.OutputPath, v => vm.OutputPath = v) { Mono = true, Browse = vm.BrowseCommand, Wide = true },
                new NoteItem(() => vm.EstimateText) { Tone = "info", Wide = true },
                new NoteItem(() => vm.ValidationError) { Tone = "danger", Wide = true, Visible = () => vm.ValidationError.Length > 0 },
            ]),
        ],
        [
            new FormButton("Cancel", vm.CancelCommand),
            new FormButton("Export", vm.ExportCommand, DialogChoiceStyle.Primary) { IsDefault = true },
        ])
    {
        Width = "640px",
        Observed = [vm, vm.Media],
        Dismiss = dismiss,
    };

    private static FormDialog Error(ErrorDialogViewModel vm, Action dismiss) => new(
        vm.Title,
        [
            new FormSection(null,
            [
                new NoteItem(() => vm.Message) { Tone = "danger" },
                new CodeItem("Technical details", () => vm.Details),
                new InfoField("Log folder", () => vm.LogFolder) { Mono = true, Wide = true },
            ]),
        ],
        [
            new FormButton("Copy details", vm.CopyDetailsCommand) { IsSecondary = true },
            new FormButton("Open log folder", vm.OpenLogFolderCommand) { IsSecondary = true },
            new FormButton("OK", vm.OkCommand, DialogChoiceStyle.Primary) { IsDefault = true },
        ])
    {
        Width = vm.HasDetails ? "720px" : "520px",
        Observed = [vm],
        Dismiss = dismiss,
    };

    internal static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
