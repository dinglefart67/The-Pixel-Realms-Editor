
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

        // =====================================================
        // ANIMATION PLAYER
        // =====================================================

        private readonly DispatcherTimer animationTimer;
        private readonly Stopwatch animationClock = new();

        private double lastClockSeconds;
        private double frameElapsed;
        private double actionElapsed;
        private int frameIndex;

        private Guid? playingActionId;
        private bool previewPaused;
        private bool facingRight = true;

        private static readonly double[] IdleDurations =
        {
            0.5, 0.05, 0.5, 0.05
        };

        private const double WalkingPreviewSeconds = 5.0;
        private const double WalkingFrameSeconds = 0.08;
        private const double MeleeFrameSeconds = 0.05;

        public EntityEditorWindow()
        {
            InitializeComponent();

            ActionTypeDatabase.Load();

            animationTimer = new DispatcherTimer(
                DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };

            animationTimer.Tick += AnimationTimer_Tick;

            animationClock.Start();
            lastClockSeconds = animationClock.Elapsed.TotalSeconds;
            animationTimer.Start();

            RefreshUi();
            UpdateFacingDirection();

            Loaded += (_, _) =>
            {
                if (initialDialogShown)
                    return;

                initialDialogShown = true;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (entity == null)
                        NewEntity();
                }), DispatcherPriority.Background);
            };
        }

        private EntityAction? IdleAction()
        {
            return entity?.Actions.FirstOrDefault(a =>
                a.Type.Equals("Idle",
                    StringComparison.OrdinalIgnoreCase));
        }

        private EntityAction? SelectedAction()
        {
            return entity?.Actions.FirstOrDefault(a =>
                a.Id == selectedActionId);
        }

        private EntityAction? PlayingAction()
        {
            return entity?.Actions.FirstOrDefault(a =>
                a.Id == playingActionId);
        }

        private static bool IsAction(
            EntityAction? action, string name)
        {
            return action != null &&
                action.Type.Equals(
                    name, StringComparison.OrdinalIgnoreCase);
        }

        private void ResetAnimationTiming()
        {
            frameIndex = 0;
            frameElapsed = 0;
            actionElapsed = 0;
            lastClockSeconds = animationClock.Elapsed.TotalSeconds;
        }

        private void StartIdle(bool play = true)
        {
            playingActionId = IdleAction()?.Id;
            previewPaused = !play;

            ResetAnimationTiming();
            RefreshAnimationPlayer();
        }

        private void StartAction(EntityAction action)
        {
            if (action.Frames.Count == 0)
            {
                StatusText.Text =
                    $"Az Action nem tartalmaz képkockát: {action.Type}";
                return;
            }

            if (IsAction(action, "Idle"))
            {
                StartIdle();
                selectedActionId = action.Id;
                RefreshActionList();
                return;
            }

            playingActionId = action.Id;
            selectedActionId = action.Id;
            previewPaused = false;

            ResetAnimationTiming();
            RefreshAnimationPlayer();
            RefreshActionList();

            StatusText.Text =
                $"{action.Type} animáció elindítva.";
        }

        private void PauseAction(EntityAction action)
        {
            if (IsAction(action, "Idle"))
            {
                StartIdle(false);
                return;
            }

            // Egy nem-Idle Action leállításakor
            // automatikusan az Idle indul el.
            StartIdle();
            selectedActionId = IdleAction()?.Id;
            RefreshActionList();
        }

        private void AnimationTimer_Tick(
            object? sender, EventArgs e)
        {
            double now = animationClock.Elapsed.TotalSeconds;
            double delta = Math.Max(
                0, now - lastClockSeconds);

            lastClockSeconds = now;

            // Modal ablak, alkalmazásváltás vagy hosszabb
            // szünet után ne ugorjon át sok képkockát.
            delta = Math.Min(delta, 0.1);

            if (previewPaused)
                return;

            EntityAction? action = PlayingAction();

            if (action == null || action.Frames.Count == 0)
                return;

            frameElapsed += delta;
            actionElapsed += delta;

            bool idle = IsAction(action, "Idle");
            bool walking = IsAction(action, "Walking");
            bool melee = IsAction(action, "Melee");

            if (walking &&
                actionElapsed >= WalkingPreviewSeconds)
            {
                ReturnToIdleAfterAction();
                return;
            }

            int transitions = 0;

            while (transitions < 100)
            {
                double duration =
                    GetFrameDuration(action, frameIndex);

                if (frameElapsed < duration)
                    break;

                frameElapsed -= duration;
                frameIndex++;
                transitions++;

                if (frameIndex >= action.Frames.Count)
                {
                    if (idle || walking)
                    {
                        frameIndex = 0;
                    }
                    else
                    {
                        // A Melee és az egyéni Actionök
                        // egyszer futnak le.
                        ReturnToIdleAfterAction();
                        return;
                    }
                }
            }

            RefreshAnimationFrame();
        }

        private static double GetFrameDuration(
            EntityAction action, int index)
        {
            if (IsAction(action, "Idle"))
            {
                return IdleDurations[
                    index % IdleDurations.Length];
            }

            if (IsAction(action, "Walking"))
                return WalkingFrameSeconds;

            if (IsAction(action, "Melee"))
                return MeleeFrameSeconds;

            return Math.Max(
                0.001, action.FrameDuration);
        }

        private void ReturnToIdleAfterAction()
        {
            StartIdle();

            selectedActionId = IdleAction()?.Id;
            RefreshActionList();
        }

        private void RefreshAnimationFrame()
        {
            EntityAction? action = PlayingAction();

            if (action == null ||
                action.Frames.Count == 0)
            {
                PreviewImage.Source = null;
                PreviewPlaceholder.Visibility =
                    Visibility.Visible;
                return;
            }

            int safeIndex = Math.Clamp(
                frameIndex, 0, action.Frames.Count - 1);

            var frame = action.Frames[safeIndex];

            PreviewImage.Source =
                EntityStorage.LoadFrame(
                    frame.ImageFileName);

            PreviewPlaceholder.Visibility =
                PreviewImage.Source == null
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PreviewFrameCountText.Text =
                $"Frame {safeIndex + 1}/{action.Frames.Count} " +
                $"• {action.Type}";
        }

        private void RefreshAnimationPlayer()
        {
            EntityAction? action = PlayingAction();

            PreviewActionTitle.Text =
                action?.Type.ToUpperInvariant()
                ?? "ELŐNÉZET";

            PreviewPlaybackStatus.Text = previewPaused
                ? "Szüneteltetve – Idle első képkocka"
                : action == null
                    ? "Nincs animáció"
                    : IsAction(action, "Walking")
                        ? $"Walking – {Math.Max(0, WalkingPreviewSeconds - actionElapsed):0.0} mp van hátra"
                        : "Animáció lejátszása";

            PreviewPlayPauseButton.Content =
                previewPaused
                    ? "▶ Start"
                    : "⏸ Pause";

            PreviewEditButton.IsEnabled =
                SelectedAction() != null;

            RefreshAnimationFrame();
        }

        private void PreviewPlayPause_Click(
            object sender, RoutedEventArgs e)
        {
            if (previewPaused)
                StartIdle();
            else
                StartIdle(false);

            RefreshActionList();
        }

        private void FaceLeft_Click(
            object sender, RoutedEventArgs e)
        {
            facingRight = false;
            UpdateFacingDirection();
        }

        private void FaceRight_Click(
            object sender, RoutedEventArgs e)
        {
            facingRight = true;
            UpdateFacingDirection();
        }

        private void UpdateFacingDirection()
        {
            PreviewDirectionTransform.ScaleX =
                facingRight ? 1 : -1;

            FacingDirectionText.Text =
                facingRight ? "Jobbra néz" : "Balra néz";

            FaceLeftButton.Opacity =
                facingRight ? 0.55 : 1.0;

            FaceRightButton.Opacity =
                facingRight ? 1.0 : 0.55;
        }

        private void ActionPlaybackButton_Click(
            object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not Guid id ||
                entity == null)
                return;

            EntityAction? action =
                entity.Actions.FirstOrDefault(a =>
                    a.Id == id);

            if (action == null)
                return;

            bool currentlyPlaying =
                !previewPaused &&
                playingActionId == action.Id;

            if (currentlyPlaying)
                PauseAction(action);
            else
                StartAction(action);

            RefreshAnimationPlayer();
            RefreshActionList();
        }

        // The entity dimensions are derived from every frame of every Action.
        // Imported sprites use the same 1:16 scale as the Map Editor.
        private void RecalculateEntitySize()
        {
            if (entity == null)
                return;

            var frames = entity.Actions.SelectMany(action => action.Frames).ToList();
            int maxWidth = frames.Select(frame => frame.PixelWidth).DefaultIfEmpty(0).Max();
            int maxHeight = frames.Select(frame => frame.PixelHeight).DefaultIfEmpty(0).Max();

            entity.Stats.Width = maxWidth / 16.0;
            entity.Stats.Height = maxHeight / 16.0;
        }

        // =====================================================
        // DOCUMENTS
        // =====================================================

        private bool ConfirmDiscard()
        {
            if (entity == null || !hasUnsavedChanges)
                return true;

            return MessageBox.Show(
                "Az entitás nem mentett módosításokat tartalmaz.\n" +
                "Folytatod mentés nélkül?",
                "Nem mentett változtatások",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
                == MessageBoxResult.Yes;
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

            EntityAction idle = new()
            {
                Type = "Idle",
                FrameDuration = 0.5
            };

            entity = new EntityDefinition
            {
                Name = name,
                Actions = new List<EntityAction> { idle }
            };

            currentPath = null;
            selectedActionId = idle.Id;
            hasUnsavedChanges = true;

            undoStack.Clear();
            redoStack.Clear();

            StartIdle();
            RefreshUi();

            // Első létrehozáskor kötelező Idle editor.
            EditAction(idle);
        }

        private void OpenEntity()
        {
            if (!ConfirmDiscard())
                return;

            OpenFileDialog dialog = new()
            {
                Title = "Entitás megnyitása",
                Filter = "The Pixel Realms entitás (*.tprentity)|*.tprentity|JSON (*.json)|*.json",
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

                double previousWidth = entity.Stats.Width;
                double previousHeight = entity.Stats.Height;
                RecalculateEntitySize();

                selectedActionId = IdleAction()?.Id;
                hasUnsavedChanges = previousWidth != entity.Stats.Width ||
                                    previousHeight != entity.Stats.Height;

                undoStack.Clear();
                redoStack.Clear();

                StartIdle();
                RefreshUi();

                int missing = entity.Actions
                    .SelectMany(a => a.Frames)
                    .Count(f => EntityStorage.LoadFrame(
                        f.ImageFileName) == null);

                if (missing > 0)
                {
                    MessageBox.Show(
                        $"{missing} PNG nem található " +
                        "az Editor helyi Assets mappájában.",
                        "Hiányzó képek",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText.Text =
                    $"Megnyitva: {Path.GetFileName(currentPath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Megnyitási hiba:\n{ex.Message}",
                    "Hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SaveEntity(bool saveAs)
        {
            if (entity == null)
                return;

            if (!ApplyInspectorChanges())
                return;

            string? path = currentPath;

            if (saveAs || string.IsNullOrWhiteSpace(path))
            {
                string safeName = string.Concat(
                    entity.Name.Split(
                        Path.GetInvalidFileNameChars()));

                SaveFileDialog dialog = new()
                {
                    Title = "Entitás mentése",
                    Filter = "The Pixel Realms entitás (*.tprentity)|*.tprentity",
                    DefaultExt = ".tprentity",
                    AddExtension = true,
                    FileName = string.IsNullOrWhiteSpace(safeName)
                        ? "Entity"
                        : safeName
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

                StatusText.Text =
                    $"Mentve: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Mentési hiba:\n{ex.Message}",
                    "Hiba",
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
        }

        // =====================================================
        // ACTION MANAGEMENT
        // =====================================================

        private void AddAction()
        {
            if (entity == null)
                return;

            List<string> available =
                ActionTypeDatabase.Types
                .Where(type =>
                    !entity.Actions.Any(a =>
                        a.Type.Equals(type,
                            StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (available.Count == 0)
            {
                MessageBox.Show(
                    "Nincs több elérhető típus.\n" +
                    "Hozz létre egyéni Action-típust.",
                    "Actions",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string? type = PromptActionType(available);

            if (!string.IsNullOrWhiteSpace(type))
                AddActionOfType(type);
        }

        private void AddActionOfType(string type)
        {
            if (entity == null)
                return;

            if (entity.Actions.Any(a =>
                a.Type.Equals(type,
                    StringComparison.OrdinalIgnoreCase)))
                return;

            EntityAction action = new()
            {
                Type = type,
                FrameDuration = type.Equals(
                    "Idle", StringComparison.OrdinalIgnoreCase)
                    ? 0.5
                    : type.Equals(
                        "Melee", StringComparison.OrdinalIgnoreCase)
                        ? MeleeFrameSeconds
                        : WalkingFrameSeconds
            };

            ActionFramesWindow window = new(action)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            HistoryState before = CaptureHistory();

            entity.Actions.Add(window.EditedAction);
            selectedActionId = window.EditedAction.Id;
            RecalculateEntitySize();

            CommitHistory(before);
            RefreshUi();
        }

        private void EditAction(EntityAction action)
        {
            if (entity == null)
                return;

            ActionFramesWindow window = new(action)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            int index = entity.Actions.FindIndex(a =>
                a.Id == action.Id);

            if (index < 0)
                return;

            HistoryState before = CaptureHistory();

            entity.Actions[index] = window.EditedAction;
            selectedActionId = window.EditedAction.Id;
            RecalculateEntitySize();

            CommitHistory(before);

            // Képkockák módosítása után a lejátszást
            // biztonságosan újrakezdjük.
            StartIdle();
            RefreshUi();
        }

        private void DeleteAction()
        {
            if (entity == null)
                return;

            EntityAction? action = SelectedAction();

            if (action == null)
                return;

            if (IsAction(action, "Idle"))
            {
                MessageBox.Show(
                    "Az Idle kötelező, ezért nem törölhető.",
                    "Actions",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show(
                $"Törlöd ezt az Actiont: {action.Type}?",
                "Action törlése",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;

            HistoryState before = CaptureHistory();

            entity.Actions.Remove(action);
            selectedActionId = IdleAction()?.Id;
            RecalculateEntitySize();

            CommitHistory(before);

            StartIdle();
            RefreshUi();
        }

        private void AddCustomAction()
        {
            if (entity == null)
                return;

            string? type = PromptText(
                "Egyéni Action-típus",
                "Az új Action neve:",
                "");

            if (string.IsNullOrWhiteSpace(type))
                return;

            try
            {
                if (!ActionTypeDatabase.Add(type))
                {
                    MessageBox.Show(
                        "Ez a típus már létezik.",
                        "Actions",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                AddActionOfType(type.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message, "Hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =====================================================
        // ACTION LIST
        // =====================================================

        private void RefreshActionList()
        {
            updatingUi = true;

            try
            {
                ActionListBox.Items.Clear();

                if (entity == null)
                    return;

                foreach (EntityAction action in entity.Actions)
                {
                    ListBoxItem item = new()
                    {
                        Tag = action.Id,
                        Padding = new Thickness(5),
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch
                    };

                    Grid grid = new();

                    grid.ColumnDefinitions.Add(
                        new ColumnDefinition
                        {
                            Width = new GridLength(53)
                        });

                    grid.ColumnDefinitions.Add(
                        new ColumnDefinition
                        {
                            Width = new GridLength(
                                1, GridUnitType.Star)
                        });

                    Border imageBorder = new()
                    {
                        Width = 47,
                        Height = 47,
                        Background = Brushes.Black,
                        BorderBrush =
                            (Brush)FindResource("BorderBrush"),
                        BorderThickness = new Thickness(1)
                    };

                    EntityFrame? first =
                        action.Frames.FirstOrDefault();

                    if (first != null)
                    {
                        Image image = new()
                        {
                            Source = EntityStorage.LoadFrame(
                                first.ImageFileName),
                            Stretch = Stretch.Uniform
                        };

                        RenderOptions.SetBitmapScalingMode(
                            image,
                            BitmapScalingMode.NearestNeighbor);

                        imageBorder.Child = image;
                    }

                    Grid.SetColumn(imageBorder, 0);
                    grid.Children.Add(imageBorder);

                    StackPanel right = new()
                    {
                        Margin = new Thickness(5, 0, 0, 0)
                    };

                    right.Children.Add(new TextBlock
                    {
                        Text = action.Type,
                        FontWeight = FontWeights.SemiBold,
                        Foreground =
                            (Brush)FindResource("TextBrush")
                    });

                    right.Children.Add(new TextBlock
                    {
                        Text = $"{action.Frames.Count} frame",
                        FontSize = 10,
                        Margin = new Thickness(0, 3, 0, 5),
                        Foreground =
                            (Brush)FindResource("MutedTextBrush")
                    });

                    bool active =
                        !previewPaused &&
                        playingActionId == action.Id;

                    Button playbackButton = new()
                    {
                        Tag = action.Id,
                        Content = active
                            ? "⏸ Pause"
                            : "▶ Start",
                        Height = 27,
                        Padding = new Thickness(0),
                        FontSize = 11
                    };

                    playbackButton.Click +=
                        ActionPlaybackButton_Click;

                    right.Children.Add(playbackButton);

                    Grid.SetColumn(right, 1);
                    grid.Children.Add(right);

                    item.Content = grid;
                    ActionListBox.Items.Add(item);

                    if (selectedActionId == action.Id)
                        ActionListBox.SelectedItem = item;
                }

                if (ActionListBox.SelectedItem == null &&
                    ActionListBox.Items.Count > 0)
                {
                    ActionListBox.SelectedIndex = 0;

                    if (ActionListBox.SelectedItem
                        is ListBoxItem item &&
                        item.Tag is Guid id)
                    {
                        selectedActionId = id;
                    }
                }

                UpdateActionButtons();
            }
            finally
            {
                updatingUi = false;
            }
        }

        private void UpdateActionButtons()
        {
            EntityAction? action = SelectedAction();

            EditActionButton.IsEnabled = action != null;
            PreviewEditButton.IsEnabled = action != null;

            DeleteActionButton.IsEnabled =
                action != null &&
                !IsAction(action, "Idle");
        }

        private void RefreshUi()
        {
            updatingUi = true;

            try
            {
                EntityNameText.Text = entity?.Name
                    ?? "Nincs megnyitott entitás";

                EntityKindCombo.IsEnabled = entity != null;

                EntityKindCombo.SelectedIndex =
                    entity == null ? -1 :
                    entity.Kind == EntityKind.Enemy ? 0 : 1;

                AddActionButton.IsEnabled = entity != null;
                ApplyInspectorButton.IsEnabled = entity != null;

                BuildInspector();
                RefreshWindowTitle();
            }
            finally
            {
                updatingUi = false;
            }

            RefreshActionList();
            RefreshAnimationPlayer();
        }

        private void RefreshWindowTitle()
        {
            Title =
                "The Pixel Realms Editor - Entitásszerkesztő";

            if (entity != null)
            {
                Title += $" - {entity.Name}";

                if (hasUnsavedChanges)
                    Title += " *";
            }
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

            AddHeader("ÁLTALÁNOS");

            AddNumber("Width", "Szélesség (automatikus)",
                s.Width, (p, v) => p.Width = v, readOnly: true);

            AddNumber("Height", "Magasság (automatikus)",
                s.Height, (p, v) => p.Height = v, readOnly: true);

            AddNumber("WalkSpeed", "Mozgási sebesség",
                s.WalkSpeed, (p, v) => p.WalkSpeed = v);

            AddNumber("PatrolWaitTime",
                "Járőr várakozás (mp)",
                s.PatrolWaitTime,
                (p, v) => p.PatrolWaitTime = v);

            if (entity.Kind == EntityKind.Enemy)
            {
                AddHeader("ELLENFÉL");

                AddNumber("MaxHp", "Maximum HP",
                    s.MaxHp, (p, v) => p.MaxHp = v);

                AddNumber("AggroSpeed", "Aggro sebesség",
                    s.AggroSpeed,
                    (p, v) => p.AggroSpeed = v);

                AddNumber("AggroRange", "Aggro távolság",
                    s.AggroRange,
                    (p, v) => p.AggroRange = v);

                AddNumber("ContactDamage", "Sebzés",
                    s.ContactDamage,
                    (p, v) => p.ContactDamage = v);

                AddNumber("DamageCooldown",
                    "Sebzési cooldown",
                    s.DamageCooldown,
                    (p, v) => p.DamageCooldown = v);

                AddNumber("AttackRange",
                    "Támadási távolság",
                    s.AttackRange,
                    (p, v) => p.AttackRange = v);

                AddNumber("StopDistance",
                    "Megállási távolság",
                    s.StopDistance,
                    (p, v) => p.StopDistance = v);

                AddNumber("AggroShareRadius",
                    "Aggro-megosztási sugár",
                    s.AggroShareRadius,
                    (p, v) => p.AggroShareRadius = v);

                AddNumber("HpBarWidth",
                    "HP-sáv szélessége",
                    s.HpBarWidth,
                    (p, v) => p.HpBarWidth = v);

                AddNumber("MeleeCooldown",
                    "Melee cooldown",
                    s.MeleeCooldown,
                    (p, v) => p.MeleeCooldown = v);

                AddNumber("FlySpeed",
                    "Repülési sebesség",
                    s.FlySpeed,
                    (p, v) => p.FlySpeed = v);

                AddHeader("ELLENFÉL – KAPCSOLÓK");

                AddBool("AggroOnSight",
                    "Automatikus aggro",
                    s.AggroOnSight,
                    (p, v) => p.AggroOnSight = v);

                AddBool("IgnoresGravity",
                    "Gravitáció figyelmen kívül",
                    s.IgnoresGravity,
                    (p, v) => p.IgnoresGravity = v);

                AddBool("CountsForKill",
                    "Beszámít a kill számlálóba",
                    s.CountsForKill,
                    (p, v) => p.CountsForKill = v);
            }
            else
            {
                AddHeader("NPC");

                AddText("DisplayName",
                    "Megjelenített név",
                    s.DisplayName,
                    (p, v) => p.DisplayName = v);

                AddText("DialogueFile",
                    "Dialógusfájl",
                    s.DialogueFile,
                    (p, v) => p.DialogueFile = v);

                AddText("InteractionHintText",
                    "Interakciós felirat",
                    s.InteractionHintText,
                    (p, v) => p.InteractionHintText = v);
            }
        }

        private void AddHeader(string title)
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

        private void AddLabel(string title)
        {
            InspectorFields.Children.Add(new TextBlock
            {
                Text = title,
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
            Action<EntityStats, double> setter,
            bool readOnly = false)
        {
            AddLabel(label);

            TextBox box = new()
            {
                Text = value.ToString(
                    "0.###", CultureInfo.CurrentCulture),
                Margin = new Thickness(0, 0, 0, 12),
                IsReadOnly = readOnly,
                Focusable = !readOnly
            };

            if (readOnly)
            {
                box.Background = new SolidColorBrush(Color.FromRgb(32, 36, 44));
                box.Foreground = (Brush)FindResource("AccentBrush");
                box.ToolTip = "Az összes Action legnagyobb PNG képkockamérete / 16.";
            }

            InspectorFields.Children.Add(box);
            if (!readOnly)
                numberFields[key] = (box, setter);
        }

        private void AddText(
            string key,
            string label,
            string value,
            Action<EntityStats, string> setter)
        {
            AddLabel(label);

            TextBox box = new()
            {
                Text = value,
                Margin = new Thickness(0, 0, 0, 12)
            };

            InspectorFields.Children.Add(box);
            textFields[key] = (box, setter);
        }

        private void AddBool(
            string key,
            string label,
            bool value,
            Action<EntityStats, bool> setter)
        {
            CheckBox check = new()
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

        private bool ApplyInspectorChanges()
        {
            if (entity == null)
                return false;

            Dictionary<string, double> values = new();

            foreach (var field in numberFields)
            {
                string input = field.Value.Control.Text;

                bool valid = double.TryParse(
                    input,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double number);

                if (!valid)
                {
                    valid = double.TryParse(
                        input,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out number);
                }

                bool positive =
                    field.Key == "Width" ||
                    field.Key == "Height" ||
                    field.Key == "MaxHp";

                if (!valid ||
                    !double.IsFinite(number) ||
                    number < 0 ||
                    (positive && number <= 0))
                {
                    MessageBox.Show(
                        $"Érvénytelen érték: {field.Key}",
                        "Inspector hiba",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    field.Value.Control.Focus();
                    return false;
                }

                values[field.Key] = number;
            }

            HistoryState before = CaptureHistory();

            foreach (var field in numberFields)
            {
                field.Value.Setter(
                    entity.Stats, values[field.Key]);
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

        private void RestoreHistory(HistoryState state)
        {
            entity = state.Document.DeepClone();
            selectedActionId = state.SelectedActionId;
            RecalculateEntitySize();

            hasUnsavedChanges = true;

            StartIdle();
            RefreshUi();
        }

        private void Undo()
        {
            if (entity == null || undoStack.Count == 0)
            {
                StatusText.Text =
                    "Nincs visszavonható művelet.";
                return;
            }

            redoStack.Push(CaptureHistory());
            RestoreHistory(undoStack.Pop());
        }

        private void Redo()
        {
            if (entity == null || redoStack.Count == 0)
            {
                StatusText.Text =
                    "Nincs újra végrehajtható művelet.";
                return;
            }

            undoStack.Push(CaptureHistory());
            RestoreHistory(redoStack.Pop());
        }

        // =====================================================
        // DIALOGS
        // =====================================================

        private string? PromptText(
            string title,
            string message,
            string initial)
        {
            Window dialog = new()
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

            Grid root = new()
            {
                Margin = new Thickness(20)
            };

            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(
                    1, GridUnitType.Star)
            });

            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            TextBlock label = new()
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBox input = new()
            {
                Text = initial,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Top
            };

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

            Button cancel = new()
            {
                Content = "Mégse",
                Width = 100,
                Margin = new Thickness(0, 0, 8, 0)
            };

            Button ok = new()
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
            Window dialog = new()
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

            StackPanel root = new()
            {
                Margin = new Thickness(20)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Válaszd ki az Action típusát:",
                Margin = new Thickness(0, 0, 0, 12)
            });

            ComboBox combo = new()
            {
                ItemsSource = available,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 20)
            };

            root.Children.Add(combo);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

            Button cancel = new()
            {
                Content = "Mégse",
                Width = 100,
                Margin = new Thickness(0, 0, 8, 0)
            };

            Button ok = new()
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
                StatusText.Text =
                    "Inspector értékek alkalmazva.";
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
                UpdateActionButtons();
                // Kijelölésre nem indul el automatikusan:
                // az Action saját Start gombja indítja.
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
        }

        private void EntityWindow_PreviewKeyDown(
            object sender, KeyEventArgs e)
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

            MainWindow menu = new();
            menu.Show();
            Close();
        }

        private void ExitEditor_Click(
            object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void EntityWindow_Closing(
            object? sender, CancelEventArgs e)
        {
            if (!navigatingBack && !ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }

            animationTimer.Stop();
            animationTimer.Tick -= AnimationTimer_Tick;
            animationClock.Stop();
        }
    }
}
