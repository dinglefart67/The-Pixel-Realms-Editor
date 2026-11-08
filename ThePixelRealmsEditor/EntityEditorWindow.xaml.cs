
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ThePixelRealmsEditor.Models;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class EntityEditorWindow : Window
    {
        private EntityDefinition? entity;
        private string? currentPath;
        private Guid? selectedActionId;

        private bool updatingUi;
        private bool hasUnsavedChanges;
        private bool navigatingBack;
        private bool initialDialogShown;

        private readonly Stack<HistoryState> undoStack = new();
        private readonly Stack<HistoryState> redoStack = new();

        private readonly Dictionary<string,
            (TextBox Control, Action<EntityStats, double> Setter)>
            numberFields = new();

        private readonly Dictionary<string,
            (CheckBox Control, Action<EntityStats, bool> Setter)>
            boolFields = new();

        private readonly Dictionary<string,
            (TextBox Control, Action<EntityStats, string> Setter)>
            textFields = new();

        private sealed class HistoryState
        {
            public EntityDefinition Document { get; init; } = new();
            public Guid? SelectedActionId { get; init; }
        }

        public EntityEditorWindow()
        {
            InitializeComponent();

            try
            {
                ActionTypeDatabase.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Az Action-típusok nem tölthetők be:\n{ex.Message}",
                    "Adatbázishiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            RefreshUi();

            Loaded += (_, _) =>
            {
                if (initialDialogShown)
                    return;

                initialDialogShown = true;

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (entity == null)
                            NewEntity();
                    }),
                    DispatcherPriority.Background);
            };
        }

        // =====================================================
        // DOCUMENT
        // =====================================================

        private bool ConfirmDiscard()
        {
            if (entity == null || !hasUnsavedChanges)
                return true;

            return MessageBox.Show(
                "Az entitás nem mentett módosításokat tartalmaz.\n" +
                "Biztosan folytatod mentés nélkül?",
                "Nem mentett változtatások",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private void NewEntity()
        {
            if (!ConfirmDiscard())
                return;

            string? name = PromptText(
                "Új entitás",
                "Add meg az entitás nevét:",
                "");

            if (string.IsNullOrWhiteSpace(name))
                return;

            var newEntity = new EntityDefinition
            {
                Name = name
            };

            var idle = new EntityAction
            {
                Type = "Idle",
                FrameDuration = 0.5
            };

            newEntity.Actions.Add(idle);

            entity = newEntity;
            currentPath = null;
            selectedActionId = idle.Id;

            undoStack.Clear();
            redoStack.Clear();
            hasUnsavedChanges = true;

            RefreshUi();
            SetStatus($"Új entitás: {name}");

            // Az Idle létrehozás után rögtön megnyílik.
            EditAction(idle);
        }

        private void OpenEntity()
        {
            if (!ConfirmDiscard())
                return;

            var dialog = new OpenFileDialog
            {
                Title = "Entitás megnyitása",
                Filter =
                    "The Pixel Realms entitás (*.tprentity)|*.tprentity|" +
                    "JSON (*.json)|*.json",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                EntityDefinition loaded =
                    EntityStorage.Load(dialog.FileName);

                entity = loaded;
                currentPath = dialog.FileName;

                selectedActionId = loaded.Actions
                    .First(a => a.Type.Equals(
                        "Idle",
                        StringComparison.OrdinalIgnoreCase)).Id;

                undoStack.Clear();
                redoStack.Clear();
                hasUnsavedChanges = false;

                RefreshUi();
                SetStatus(
                    $"Entitás megnyitva: " +
                    $"{Path.GetFileName(dialog.FileName)}");

                int missing = loaded.Actions
                    .SelectMany(a => a.Frames)
                    .Count(f => EntityStorage.LoadFrame(
                        f.ImageFileName) == null);

                if (missing > 0)
                {
                    MessageBox.Show(
                        $"{missing} képkocka képfájlja hiányzik " +
                        "az Editor helyi Assets könyvtárából.\n\n" +
                        "Az entitás megnyílt, de ezeket a " +
                        "képeket újra kell importálni.",
                        "Hiányzó képek",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Az entitás nem nyitható meg:\n{ex.Message}",
                    "Megnyitási hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SaveEntity(bool saveAs)
        {
            if (entity == null)
            {
                MessageBox.Show(
                    "Előbb hozz létre vagy nyiss meg egy entitást.",
                    "Nincs entitás",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!ApplyInspectorChanges())
                return;

            EntityAction? idle = entity.Actions
                .FirstOrDefault(a => a.Type.Equals(
                    "Idle",
                    StringComparison.OrdinalIgnoreCase));

            if (idle == null || idle.Frames.Count == 0)
            {
                MessageBox.Show(
                    "A mentéshez legalább egy Idle képkocka szükséges.",
                    "Hiányzó Idle",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            int missing = entity.Actions
                .SelectMany(a => a.Frames)
                .Count(f => EntityStorage.LoadFrame(
                    f.ImageFileName) == null);

            if (missing > 0)
            {
                MessageBox.Show(
                    "A mentés előtt pótold a hiányzó " +
                    "animációs képeket.",
                    "Hiányzó PNG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string? path = currentPath;

            if (saveAs || string.IsNullOrWhiteSpace(path))
            {
                string safeName = string.Concat(
                    entity.Name.Split(
                        Path.GetInvalidFileNameChars()));

                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "Entity";

                var dialog = new SaveFileDialog
                {
                    Title = "Entitás mentése",
                    Filter =
                        "The Pixel Realms entitás (*.tprentity)|*.tprentity",
                    DefaultExt = ".tprentity",
                    AddExtension = true,
                    FileName = safeName
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                path = dialog.FileName;
            }

            try
            {
                EntityStorage.Save(entity, path!);
                currentPath = path;
                hasUnsavedChanges = false;

                RefreshWindowTitle();
                SetStatus(
                    $"Entitás mentve: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Sikertelen mentés:\n{ex.Message}",
                    "Mentési hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RenameEntity()
        {
            if (entity == null)
                return;

            string? name = PromptText(
                "Entitás átnevezése",
                "Új név:",
                entity.Name);

            if (string.IsNullOrWhiteSpace(name) ||
                name == entity.Name)
                return;

            HistoryState before = CaptureHistory();
            entity.Name = name;
            CommitHistory(before);

            RefreshUi();
            SetStatus($"Entitás átnevezve: {name}");
        }

        // =====================================================
        // ACTIONS
        // =====================================================

        private EntityAction? SelectedAction()
        {
            return entity?.Actions.FirstOrDefault(
                a => a.Id == selectedActionId);
        }

        private void AddAction()
        {
            if (entity == null)
                return;

            List<string> available =
                ActionTypeDatabase.Types
                    .Where(type => !entity.Actions.Any(a =>
                        a.Type.Equals(
                            type,
                            StringComparison.OrdinalIgnoreCase)))
                    .ToList();

            if (available.Count == 0)
            {
                MessageBox.Show(
                    "Minden ismert Action-típus szerepel " +
                    "az entitásban.\n" +
                    "Az Actionök menüben létrehozhatsz új típust.",
                    "Nincs új típus",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string? selectedType =
                PromptActionType(available);

            if (!string.IsNullOrWhiteSpace(selectedType))
                AddActionOfType(selectedType);
        }

        private void AddActionOfType(string type)
        {
            if (entity == null)
                return;

            if (entity.Actions.Any(a =>
                a.Type.Equals(
                    type, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "Ez az Action már szerepel az entitásban.",
                    "Duplikált Action",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var newAction = new EntityAction
            {
                Type = type,
                FrameDuration =
                    type.Equals(
                        "Idle",
                        StringComparison.OrdinalIgnoreCase)
                    ? 0.5
                    : 0.08
            };

            var window = new ActionFramesWindow(newAction)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            HistoryState before = CaptureHistory();

            entity.Actions.Add(window.EditedAction);
            selectedActionId = window.EditedAction.Id;

            CommitHistory(before);
            RefreshUi();
            SetStatus($"Action létrehozva: {type}");
        }

        private void EditAction(EntityAction action)
        {
            if (entity == null)
                return;

            var window = new ActionFramesWindow(action)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            HistoryState before = CaptureHistory();

            int index = entity.Actions.FindIndex(
                a => a.Id == action.Id);

            if (index < 0)
                return;

            entity.Actions[index] = window.EditedAction;
            selectedActionId = window.EditedAction.Id;

            CommitHistory(before);
            RefreshUi();
            SetStatus($"Action mentve: {action.Type}");
        }

        private void DeleteAction()
        {
            if (entity == null)
                return;

            EntityAction? action = SelectedAction();

            if (action == null)
                return;

            if (action.Type.Equals(
                "Idle", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Az Idle kötelező Action, ezért nem törölhető.",
                    "Kötelező Action",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show(
                $"Törlöd ezt az Actiont: {action.Type}?",
                "Action törlése",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            HistoryState before = CaptureHistory();

            entity.Actions.Remove(action);

            selectedActionId = entity.Actions
                .FirstOrDefault(a => a.Type.Equals(
                    "Idle",
                    StringComparison.OrdinalIgnoreCase))?.Id;

            // A képek maradnak a tárhelyen, hogy az
            // Undo vissza tudja állítani az Actiont.
            CommitHistory(before);
            RefreshUi();
            SetStatus($"Action törölve: {action.Type}");
        }

        private void AddCustomAction()
        {
            if (entity == null)
                return;

            string? type = PromptText(
                "Egyéni Action-típus",
                "Az új Action-típus neve:",
                "");

            if (string.IsNullOrWhiteSpace(type))
                return;

            try
            {
                if (!ActionTypeDatabase.Add(type))
                {
                    MessageBox.Show(
                        "Ez az Action-típus már létezik.",
                        "Létező típus",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                AddActionOfType(type.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"A típus mentése sikertelen:\n{ex.Message}",
                    "Mentési hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =====================================================
        // UI
        // =====================================================

        private void RefreshUi()
        {
            updatingUi = true;

            try
            {
                EntityNameText.Text =
                    entity?.Name ?? "Nincs megnyitott entitás";

                bool hasEntity = entity != null;

                EntityKindCombo.IsEnabled = hasEntity;
                EntityKindCombo.SelectedIndex =
                    !hasEntity ? -1 :
                    entity!.Kind == EntityKind.Enemy ? 0 : 1;

                AddActionButton.IsEnabled = hasEntity;
                ApplyInspectorButton.IsEnabled = hasEntity;

                BuildInspector();
                RefreshActionList();
                RefreshPreview();

                RefreshWindowTitle();
            }
            finally
            {
                updatingUi = false;
            }
        }

        private void RefreshWindowTitle()
        {
            Title = "The Pixel Realms Editor - Entitásszerkesztő";

            if (entity != null)
            {
                Title += $" - {entity.Name}";

                if (hasUnsavedChanges)
                    Title += " *";
            }
        }

        private void RefreshActionList()
        {
            ActionListBox.Items.Clear();

            if (entity == null)
            {
                EditActionButton.IsEnabled = false;
                DeleteActionButton.IsEnabled = false;
                return;
            }

            foreach (EntityAction action in entity.Actions)
            {
                var item = new ListBoxItem
                {
                    Tag = action.Id,
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 0, 0, 5),
                    HorizontalContentAlignment =
                        HorizontalAlignment.Stretch
                };

                var layout = new Grid();
                layout.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(55)
                    });
                layout.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(
                            1, GridUnitType.Star)
                    });

                var imageBorder = new Border
                {
                    Width = 48,
                    Height = 48,
                    Background = Brushes.Black,
                    BorderBrush =
                        (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1)
                };

                var firstFrame = action.Frames.FirstOrDefault();

                if (firstFrame != null)
                {
                    var image = new Image
                    {
                        Source = EntityStorage.LoadFrame(
                            firstFrame.ImageFileName),
                        Stretch = Stretch.Uniform
                    };

                    RenderOptions.SetBitmapScalingMode(
                        image, BitmapScalingMode.NearestNeighbor);

                    imageBorder.Child = image;
                }

                Grid.SetColumn(imageBorder, 0);
                layout.Children.Add(imageBorder);

                var textPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(7, 0, 0, 0)
                };

                textPanel.Children.Add(new TextBlock
                {
                    Text = action.Type,
                    FontWeight = FontWeights.SemiBold,
                    Foreground =
                        (Brush)FindResource("TextBrush")
                });

                textPanel.Children.Add(new TextBlock
                {
                    Text = $"{action.Frames.Count} képkocka",
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground =
                        (Brush)FindResource("MutedTextBrush")
                });

                Grid.SetColumn(textPanel, 1);
                layout.Children.Add(textPanel);

                item.Content = layout;
                ActionListBox.Items.Add(item);

                if (action.Id == selectedActionId)
                    ActionListBox.SelectedItem = item;
            }

            if (ActionListBox.SelectedItem == null &&
                ActionListBox.Items.Count > 0)
            {
                ActionListBox.SelectedIndex = 0;

                if (ActionListBox.SelectedItem is ListBoxItem item &&
                    item.Tag is Guid id)
                {
                    selectedActionId = id;
                }
            }

            UpdateActionButtons();
        }

        private void RefreshPreview()
        {
            EntityAction? action = SelectedAction();

            PreviewActionTitle.Text = action == null
                ? "ELŐNÉZET"
                : action.Type.ToUpperInvariant();

            PreviewFrameCountText.Text = action == null
                ? "0 képkocka"
                : $"{action.Frames.Count} képkocka • " +
                  $"{action.FrameDuration:0.###} mp/képkocka";

            var firstFrame = action?.Frames.FirstOrDefault();

            var image = firstFrame == null
                ? null
                : EntityStorage.LoadFrame(
                    firstFrame.ImageFileName);

            PreviewImage.Source = image;
            PreviewPlaceholder.Visibility =
                image == null
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PreviewEditButton.IsEnabled = action != null;
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            EntityAction? action = SelectedAction();

            EditActionButton.IsEnabled = action != null;

            DeleteActionButton.IsEnabled =
                action != null &&
                !action.Type.Equals(
                    "Idle",
                    StringComparison.OrdinalIgnoreCase);
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        // =====================================================
        // INSPECTOR
        // =====================================================

        private void BuildInspector()
        {
            InspectorFields.Children.Clear();
            numberFields.Clear();
            boolFields.Clear();
            textFields.Clear();

            if (entity == null)
                return;

            EntityStats s = entity.Stats;

            AddInspectorHeader("ÁLTALÁNOS");

            AddNumber("Width", "Szélesség",
                s.Width, (p, v) => p.Width = v);

            AddNumber("Height", "Magasság",
                s.Height, (p, v) => p.Height = v);

            AddNumber("WalkSpeed", "Mozgási sebesség",
                s.WalkSpeed, (p, v) => p.WalkSpeed = v);

            AddNumber("PatrolWaitTime", "Járőr várakozás (mp)",
                s.PatrolWaitTime,
                (p, v) => p.PatrolWaitTime = v);

            if (entity.Kind == EntityKind.Enemy)
            {
                AddInspectorHeader("ELLENFÉL – ALAPÉRTÉKEK");

                AddNumber("MaxHp", "Maximum HP",
                    s.MaxHp, (p, v) => p.MaxHp = v);

                AddNumber("AggroSpeed", "Aggro sebesség",
                    s.AggroSpeed, (p, v) => p.AggroSpeed = v);

                AddNumber("AggroRange", "Aggro távolság",
                    s.AggroRange, (p, v) => p.AggroRange = v);

                AddNumber("ContactDamage", "Sebzés",
                    s.ContactDamage,
                    (p, v) => p.ContactDamage = v);

                AddNumber("DamageCooldown", "Sebzési cooldown (mp)",
                    s.DamageCooldown,
                    (p, v) => p.DamageCooldown = v);

                AddNumber("AttackRange", "Támadási távolság",
                    s.AttackRange,
                    (p, v) => p.AttackRange = v);

                AddNumber("StopDistance", "Megállási távolság",
                    s.StopDistance,
                    (p, v) => p.StopDistance = v);

                AddNumber("AggroShareRadius", "Aggro-megosztási sugár",
                    s.AggroShareRadius,
                    (p, v) => p.AggroShareRadius = v);

                AddNumber("HpBarWidth", "HP-sáv szélessége",
                    s.HpBarWidth,
                    (p, v) => p.HpBarWidth = v);

                AddNumber("MeleeCooldown", "Melee cooldown (mp)",
                    s.MeleeCooldown,
                    (p, v) => p.MeleeCooldown = v);

                AddNumber("FlySpeed", "Repülési sebesség",
                    s.FlySpeed,
                    (p, v) => p.FlySpeed = v);

                AddInspectorHeader("ELLENFÉL – KAPCSOLÓK");

                AddBoolean("AggroOnSight", "Automatikus aggro",
                    s.AggroOnSight,
                    (p, v) => p.AggroOnSight = v);

                AddBoolean("IgnoresGravity", "Gravitáció figyelmen kívül",
                    s.IgnoresGravity,
                    (p, v) => p.IgnoresGravity = v);

                AddBoolean("CountsForKill", "Beszámít a kill számlálóba",
                    s.CountsForKill,
                    (p, v) => p.CountsForKill = v);
            }
            else
            {
                AddInspectorHeader("NPC");

                AddText("DisplayName", "Megjelenített név",
                    s.DisplayName,
                    (p, v) => p.DisplayName = v);

                AddText("DialogueFile", "Dialógusfájl",
                    s.DialogueFile,
                    (p, v) => p.DialogueFile = v);

                AddText("InteractionHintText",
                    "Interakciós felirat",
                    s.InteractionHintText,
                    (p, v) => p.InteractionHintText = v);
            }
        }

        private void AddInspectorHeader(string title)
        {
            InspectorFields.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground =
                    (Brush)FindResource("AccentBrush"),
                Margin = new Thickness(0, 14, 0, 11)
            });
        }

        private void AddFieldLabel(string label)
        {
            InspectorFields.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground =
                    (Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 0, 0, 5)
            });
        }

        private void AddNumber(
            string key,
            string label,
            double value,
            Action<EntityStats, double> setter)
        {
            AddFieldLabel(label);

            var box = new TextBox
            {
                Text = value.ToString(
                    "0.###", CultureInfo.CurrentCulture),
                Margin = new Thickness(0, 0, 0, 12)
            };

            InspectorFields.Children.Add(box);
            numberFields[key] = (box, setter);
        }

        private void AddText(
            string key,
            string label,
            string value,
            Action<EntityStats, string> setter)
        {
            AddFieldLabel(label);

            var box = new TextBox
            {
                Text = value,
                Margin = new Thickness(0, 0, 0, 12)
            };

            InspectorFields.Children.Add(box);
            textFields[key] = (box, setter);
        }

        private void AddBoolean(
            string key,
            string label,
            bool value,
            Action<EntityStats, bool> setter)
        {
            var check = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Foreground =
                    (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 13)
            };

            InspectorFields.Children.Add(check);
            boolFields[key] = (check, setter);
        }

        private static bool TryReadNumber(
            string value,
            out double number)
        {
            if (double.TryParse(
                value, NumberStyles.Float,
                CultureInfo.CurrentCulture, out number))
                return true;

            return double.TryParse(
                value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out number);
        }

        private bool ApplyInspectorChanges()
        {
            if (entity == null)
                return false;

            var values = new Dictionary<string, double>();

            foreach (var field in numberFields)
            {
                string key = field.Key;
                string input = field.Value.Control.Text;

                bool valid = TryReadNumber(
                    input, out double number);

                bool mustBePositive =
                    key == "Width" ||
                    key == "Height" ||
                    key == "MaxHp";

                if (!valid ||
                    !double.IsFinite(number) ||
                    number < 0 ||
                    (mustBePositive && number <= 0))
                {
                    MessageBox.Show(
                        $"Érvénytelen számérték: {key}\n" +
                        "A méretnek és a maximum HP-nak " +
                        "pozitívnak kell lennie.",
                        "Inspector hiba",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    field.Value.Control.Focus();
                    return false;
                }

                values[key] = number;
            }

            HistoryState before = CaptureHistory();

            foreach (var field in numberFields)
            {
                field.Value.Setter(
                    entity.Stats,
                    values[field.Key]);
            }

            foreach (var field in boolFields)
            {
                field.Value.Setter(
                    entity.Stats,
                    field.Value.Control.IsChecked == true);
            }

            foreach (var field in textFields)
            {
                field.Value.Setter(
                    entity.Stats,
                    field.Value.Control.Text.Trim());
            }

            CommitHistory(before);

            return true;
        }

        // =====================================================
        // UNDO / REDO
        // =====================================================

        private HistoryState CaptureHistory()
        {
            return new HistoryState
            {
                Document = entity!.DeepClone(),
                SelectedActionId = selectedActionId
            };
        }

        private void CommitHistory(HistoryState before)
        {
            if (entity == null)
                return;

            string oldJson =
                JsonSerializer.Serialize(before.Document);

            string newJson =
                JsonSerializer.Serialize(entity);

            if (oldJson == newJson)
                return;

            undoStack.Push(before);
            redoStack.Clear();

            hasUnsavedChanges = true;
            RefreshWindowTitle();
        }

        private void RestoreHistory(HistoryState snapshot)
        {
            entity = snapshot.Document.DeepClone();
            selectedActionId = snapshot.SelectedActionId;

            hasUnsavedChanges = true;
            RefreshUi();
        }

        private void Undo()
        {
            if (entity == null || undoStack.Count == 0)
            {
                SetStatus("Nincs visszavonható művelet.");
                return;
            }

            redoStack.Push(CaptureHistory());
            RestoreHistory(undoStack.Pop());

            SetStatus("Visszavonás – Ctrl+Z");
        }

        private void Redo()
        {
            if (entity == null || redoStack.Count == 0)
            {
                SetStatus("Nincs újra végrehajtható művelet.");
                return;
            }

            undoStack.Push(CaptureHistory());
            RestoreHistory(redoStack.Pop());

            SetStatus("Újra – Ctrl+Y");
        }

        // =====================================================
        // DIALOGS
        // =====================================================

        private string? PromptText(
            string title,
            string message,
            string initial)
        {
            var dialog = new Window
            {
                Title = title,
                Owner = this,
                Width = 470,
                Height = 210,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Background =
                    (Brush)FindResource("BackgroundBrush")
            };

            var root = new Grid
            {
                Margin = new Thickness(20)
            };

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = new GridLength(
                        1, GridUnitType.Star)
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });

            var label = new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var input = new TextBox
            {
                Text = initial,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Top
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button
            {
                Content = "Mégse",
                Width = 100,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var ok = new Button
            {
                Content = "OK",
                Width = 100,
                IsDefault = true
            };

            cancel.Click += (_, _) =>
                dialog.DialogResult = false;

            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(input.Text))
                {
                    MessageBox.Show(
                        "Adj meg egy nevet!",
                        "Hiányzó név",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                dialog.DialogResult = true;
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            Grid.SetRow(label, 0);
            Grid.SetRow(input, 1);
            Grid.SetRow(buttons, 2);

            root.Children.Add(label);
            root.Children.Add(input);
            root.Children.Add(buttons);

            dialog.Content = root;

            dialog.Loaded += (_, _) =>
            {
                input.Focus();
                input.SelectAll();
            };

            return dialog.ShowDialog() == true
                ? input.Text.Trim()
                : null;
        }

        private string? PromptActionType(
            List<string> available)
        {
            var dialog = new Window
            {
                Title = "Action hozzáadása",
                Owner = this,
                Width = 450,
                Height = 205,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Background =
                    (Brush)FindResource("BackgroundBrush")
            };

            var root = new StackPanel
            {
                Margin = new Thickness(20)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Válaszd ki az Action típusát:",
                Margin = new Thickness(0, 0, 0, 12)
            });

            var combo = new ComboBox
            {
                ItemsSource = available,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 20)
            };

            root.Children.Add(combo);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button
            {
                Content = "Mégse",
                Width = 100,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var ok = new Button
            {
                Content = "Hozzáadás",
                Width = 120,
                IsDefault = true
            };

            cancel.Click += (_, _) =>
                dialog.DialogResult = false;

            ok.Click += (_, _) =>
            {
                if (combo.SelectedItem != null)
                    dialog.DialogResult = true;
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            root.Children.Add(buttons);
            dialog.Content = root;

            return dialog.ShowDialog() == true
                ? combo.SelectedItem as string
                : null;
        }

        // =====================================================
        // EVENT HANDLERS
        // =====================================================

        private void NewEntity_Click(
            object sender, RoutedEventArgs e)
            => NewEntity();

        private void OpenEntity_Click(
            object sender, RoutedEventArgs e)
            => OpenEntity();

        private void SaveEntity_Click(
            object sender, RoutedEventArgs e)
            => SaveEntity(false);

        private void SaveAsEntity_Click(
            object sender, RoutedEventArgs e)
            => SaveEntity(true);

        private void RenameEntity_Click(
            object sender, RoutedEventArgs e)
            => RenameEntity();

        private void AddAction_Click(
            object sender, RoutedEventArgs e)
            => AddAction();

        private void AddCustomAction_Click(
            object sender, RoutedEventArgs e)
            => AddCustomAction();

        private void EditAction_Click(
            object sender, RoutedEventArgs e)
        {
            EntityAction? action = SelectedAction();

            if (action != null)
                EditAction(action);
        }

        private void DeleteAction_Click(
            object sender, RoutedEventArgs e)
            => DeleteAction();

        private void ApplyInspector_Click(
            object sender, RoutedEventArgs e)
        {
            if (ApplyInspectorChanges())
            {
                BuildInspector();
                SetStatus("Inspector értékek alkalmazva.");
            }
        }

        private void Undo_Click(
            object sender, RoutedEventArgs e)
            => Undo();

        private void Redo_Click(
            object sender, RoutedEventArgs e)
            => Redo();

        private void ActionListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (updatingUi)
                return;

            if (ActionListBox.SelectedItem
                is ListBoxItem item &&
                item.Tag is Guid id)
            {
                selectedActionId = id;
                RefreshPreview();
            }
        }

        private void EntityKindCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (updatingUi || entity == null ||
                EntityKindCombo.SelectedIndex < 0)
                return;

            EntityKind kind =
                EntityKindCombo.SelectedIndex == 0
                    ? EntityKind.Enemy
                    : EntityKind.Npc;

            if (entity.Kind == kind)
                return;

            HistoryState before = CaptureHistory();

            entity.Kind = kind;

            CommitHistory(before);
            BuildInspector();

            SetStatus(
                $"Entitástípus: {kind}");
        }

        private void EntityWindow_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            bool ctrl =
                (Keyboard.Modifiers & ModifierKeys.Control)
                != 0;

            bool shift =
                (Keyboard.Modifiers & ModifierKeys.Shift)
                != 0;

            if (!ctrl)
                return;

            if (e.Key == Key.Z)
                Undo();
            else if (e.Key == Key.Y)
                Redo();
            else if (e.Key == Key.S)
                SaveEntity(shift);
            else if (e.Key == Key.O)
                OpenEntity();
            else if (e.Key == Key.N)
                NewEntity();
            else
                return;

            e.Handled = true;
        }

        private void BackToMenu_Click(
            object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscard())
                return;

            navigatingBack = true;

            var menu = new MainWindow();
            menu.Show();

            Close();
        }

        private void ExitEditor_Click(
            object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void EntityWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (!navigatingBack && !ConfirmDiscard())
                e.Cancel = true;
        }
    }
}
