using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Interface;
using Dalamud.Logging;
using DeathRecap.Messages;
using ImGuiNET;
using ImGuiScene;
using Lumina.Excel.GeneratedSheets;

namespace DeathRecap {
    public class DeathRecapWindow {
        private static readonly Vector4 ColorHealing = new(0.8196079F, 0.9803922F, 0.6F, 1);
        private static readonly Vector4 ColorDamage = new(0.9019608F, 0.5019608F, 0.4F, 1);
        private static readonly Vector4 ColorMagicDamage = new(0.145098F, 0.6F, 0.7450981F, 1);
        private static readonly Vector4 ColorPhysicalDamage = new(1F, 0.6392157F, 0.2901961F, 1);
        private static readonly Vector4 ColorAction = new(0.7215686F, 0.6588235F, 0.9411765F, 1);
        private static readonly Vector4 ColorGrey = new(0.5019608F, 0.5019608F, 0.5019608F, 1);

        private readonly DeathRecapPlugin plugin;

        private readonly Dictionary<ushort, TextureWrap> textures = new();

        public bool ShowDeathRecap { get; internal set; }

        private int selectedDeath;
        private uint selectedPlayer;

        private bool hasShownTip;

        public DeathRecapWindow(DeathRecapPlugin plugin) {
            this.plugin = plugin;
        }

        public void Draw() {
            try {
                var elapsed = (DateTime.Now - plugin.LastDeath?.TimeOfDeath)?.TotalSeconds;
                if (!ShowDeathRecap && elapsed < 30) {
                    var viewport = ImGui.GetMainViewport();
                    ImGui.SetNextWindowPos(new Vector2(viewport.WorkPos.X + viewport.WorkSize.X / 2 - 100 * ImGuiHelpers.GlobalScale, viewport.WorkPos.Y + viewport.WorkSize.Y / 2 - 40 * ImGuiHelpers.GlobalScale), ImGuiCond.FirstUseEver);
                    ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(200, 80));
                    if (ImGui.Begin("(Drag me somewhere)", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)) {
                        var label = $"Show Death Recap ({30 - elapsed:N0}s)";
                        if (plugin.LastDeath?.PlayerName is { } playerName) {
                            label = AppendCenteredPlayerName(label, playerName);
                        }
                        if (ImGui.Button(label, new Vector2(-1, -1))) {
                            ShowDeathRecap = true;
                            if (plugin.LastDeath?.PlayerId is { } id) {
                                selectedPlayer = id;
                            }
                        }
                    }
                }
                if (!ShowDeathRecap) return;
                var bShowDeathRecap = ShowDeathRecap;
                ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(800, 350), ImGuiCond.FirstUseEver);
                if (ImGui.Begin("Death Recap", ref bShowDeathRecap, ImGuiWindowFlags.NoCollapse)) {
                    if (!plugin.DeathsPerPlayer.TryGetValue(selectedPlayer, out var deaths))
                        deaths = new List<Death>();

                    DrawPlayerSelection(deaths.FirstOrDefault()?.PlayerName);

                    ImGui.SameLine(ImGui.GetWindowContentRegionWidth() - ImGuiHelpers.GetButtonSize("Clear").X);
                    if (ImGui.Button("Clear")) {
                        plugin.DeathsPerPlayer.Clear();
                    }

                    ImGui.Separator();

                    if (selectedDeath < 0 || selectedDeath >= deaths.Count)
                        selectedDeath = 0;

                    ImGui.Columns(2);
                    ImGui.SetColumnWidth(0, 160 * ImGuiHelpers.GlobalScale);
                    ImGui.TextUnformatted("Deaths");
                    for (var index = deaths.Count - 1; index >= 0; index--) {
                        ImGui.PushID(index);
                        if (ImGui.SmallButton("x")) {
                            if (deaths.Count - 1 - selectedDeath < index) {
                                selectedDeath--;
                            }
                            deaths.RemoveAt(index--);
                        } else {
                            ImGui.SameLine();
                            if (ImGui.Selectable(deaths[index].Title, index == deaths.Count - 1 - selectedDeath)) {
                                selectedDeath = deaths.Count - 1 - index;
                            }
                        }

                        ImGui.PopID();
                    }

                    ImGui.NextColumn();

                    DrawCombatEventTable(deaths.Count > selectedDeath ? deaths[deaths.Count - 1 - selectedDeath] : null);

                    ImGui.End();

                    if (!bShowDeathRecap) {
                        ShowDeathRecap = false;
                        plugin.LastDeath = null;
                        if (!hasShownTip) {
                            Service.ChatGui.Print("[DeathRecap] Tip: You can reopen this window using /dr or /deathrecap");
                            hasShownTip = true;
                        }
                    }
                }
            } catch (Exception e) {
                PluginLog.Error(e, "Failed to draw window");
            }
        }

        private static string AppendCenteredPlayerName(string label, string pname) {
            var length = ImGui.CalcTextSize(label).X;
            var spclength = ImGui.CalcTextSize(" ").X;
            var namelength = ImGui.CalcTextSize(pname).X;
            var spccount = (int)Math.Round((namelength - length) / 2f / spclength);
            if (spccount == 0) return label + "\n" + pname;
            if (spccount > 0) {
                var strbld = new StringBuilder(spccount * 2 + label.Length + pname.Length + 1);
                strbld.Append(' ', spccount);
                strbld.Append(label);
                strbld.Append(' ', spccount);
                strbld.Append('\n');
                strbld.Append(pname);
                return strbld.ToString();
            } else {
                var strbld = new StringBuilder(-spccount * 2 + label.Length + pname.Length + 1);
                strbld.Append(label);
                strbld.Append('\n');
                strbld.Append(' ', -spccount);
                strbld.Append(pname);
                strbld.Append(' ', -spccount);
                return strbld.ToString();
            }
        }

        private void DrawPlayerSelection(string? selectedPlayerName) {
            void DrawItem(IEnumerable<Death> pdeaths, uint id) {
                if (pdeaths.FirstOrDefault()?.PlayerName is not { } name) return;
                if (ImGui.Selectable(name, id == selectedPlayer)) {
                    selectedPlayer = id;
                }
            }

            ImGui.TextUnformatted("Player");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("", selectedPlayerName)) {
                var processed = new HashSet<uint>();

                if (Service.PartyList.Length > 0) {
                    foreach (var pmem in Service.PartyList) {
                        var id = pmem.ObjectId;
                        if (processed.Contains(id) || !plugin.DeathsPerPlayer.TryGetValue(id, out var pdeaths)) continue;
                        DrawItem(pdeaths, id);
                        processed.Add(id);
                    }
                } else if (Service.ObjectTable[0]?.ObjectId is { } localPlayerId && plugin.DeathsPerPlayer.TryGetValue(localPlayerId, out var pdeaths)) {
                    DrawItem(pdeaths, localPlayerId);
                    processed.Add(localPlayerId);
                }

                foreach (var (id, pdeaths) in plugin.DeathsPerPlayer) {
                    if (processed.Contains(id)) continue;
                    DrawItem(pdeaths, id);
                }

                ImGui.EndCombo();
            }
        }

        private void DrawCombatEventTable(Death? death) {
            if (ImGui.BeginTable("deathrecap", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable)) {
                ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Ability");
                ImGui.TableSetupColumn("Source");
                ImGui.TableSetupColumn("HP Before");
                ImGui.TableSetupColumn("Status Effects");
                ImGui.TableHeadersRow();

                if (death != null) {
                    for (var i = death.Events.Count - 1; i >= 0; i--) {
                        switch (death.Events[i]) {
                            case CombatEvent.HoT hot:
                                ImGui.TableNextRow();

                                DrawTimeColumn(hot, death.TimeOfDeath);

                                var total = hot.Amount;
                                while (i > 0 && death.Events[i - 1] is CombatEvent.HoT h) {
                                    hot = h;
                                    total += h.Amount;
                                    i--;
                                }

                                ImGui.TableNextColumn(); // Amount
                                ImGui.TextColored(ColorHealing, $"+{total:N0}");

                                ImGui.TableNextColumn(); // Ability
                                ImGui.TextUnformatted("Regen");

                                ImGui.TableNextColumn(); // Source

                                DrawHpColumn(hot);
                                break;
                            case CombatEvent.DoT dot:
                                ImGui.TableNextRow();

                                DrawTimeColumn(dot, death.TimeOfDeath);

                                ImGui.TableNextColumn(); // Amount
                                ImGui.TextColored(ColorDamage, $"-{dot.Amount:N0}");

                                ImGui.TableNextColumn(); // Ability
                                ImGui.TextUnformatted($"DoT damage");

                                ImGui.TableNextColumn(); // Source

                                DrawHpColumn(dot);
                                break;
                            case CombatEvent.DamageTaken dt: {
                                ImGui.TableNextRow();

                                DrawTimeColumn(dt, death.TimeOfDeath);

                                ImGui.TableNextColumn(); // Amount
                                var text = $"-{dt.Amount:N0}{(dt.Crit ? dt.DirectHit ? "!!" : "!" : "")}";
                                if (dt.DamageType == DamageType.Magic) {
                                    ImGui.TextColored(ColorMagicDamage, text);
                                } else {
                                    ImGui.TextColored(ColorPhysicalDamage, text);
                                }

                                if (ImGui.IsItemHovered()) {
                                    ImGui.BeginTooltip();
                                    ImGui.TextUnformatted($"{dt.DamageType} Damage");
                                    if (dt.Crit) ImGui.TextUnformatted("Critical Hit");
                                    if (dt.DirectHit) ImGui.TextUnformatted("Direct Hit (+25%)");
                                    if (dt.Parried) ImGui.TextUnformatted("Parried (-20%)");
                                    if (dt.Blocked) ImGui.TextUnformatted("Blocked (-15%)");
                                    ImGui.EndTooltip();
                                }

                                ImGui.TableNextColumn(); // Ability
                                if (dt.DisplayType != ActionEffectDisplayType.HideActionName) {
                                    if (GetIconImage(dt.Icon) is {} img) {
                                        ImGui.Image(img.ImGuiHandle, ImGuiHelpers.ScaledVector2(16, 16));
                                        ImGui.SameLine();
                                    }

                                    ImGui.TextColored(ColorAction, $"{dt.Action}");
                                }

                                ImGui.TableNextColumn(); // Source
                                ImGui.TextUnformatted(dt.Source ?? "");

                                DrawHpColumn(dt);

                                DrawStatusEffectsColumn(dt);
                                break;
                            }
                            case CombatEvent.Healed h: {
                                ImGui.TableNextRow();

                                DrawTimeColumn(h, death.TimeOfDeath);

                                ImGui.TableNextColumn(); // Amount
                                ImGui.TextColored(ColorHealing, $"+{h.Amount:N0}");

                                ImGui.TableNextColumn(); // Ability
                                if (GetIconImage(h.Icon) is {} img) {
                                    ImGui.Image(img.ImGuiHandle, ImGuiHelpers.ScaledVector2(16, 16));
                                    ImGui.SameLine();
                                }

                                ImGui.TextColored(ColorAction, h.Action ?? "");

                                ImGui.TableNextColumn(); // Source
                                ImGui.TextUnformatted(h.Source ?? "");

                                DrawHpColumn(h);

                                DrawStatusEffectsColumn(h);
                                break;
                            }
                            case CombatEvent.StatusEffect s: {
                                ImGui.TableNextRow();

                                DrawTimeColumn(s, death.TimeOfDeath);

                                ImGui.TableNextColumn(); // Amount
                                ImGui.TextUnformatted($"{s.Duration:N0}s");

                                ImGui.TableNextColumn(); // Ability
                                if (GetIconImage(s.Icon) is {} img) {
                                    ImGui.Image(img.ImGuiHandle, ImGuiHelpers.ScaledVector2(16, 16));
                                    ImGui.SameLine();
                                }

                                ImGui.TextUnformatted(s.Status ?? "");
                                if (ImGui.IsItemHovered()) {
                                    ImGui.BeginTooltip();
                                    ImGui.TextUnformatted(s.Description);
                                    ImGui.EndTooltip();
                                }

                                ImGui.TableNextColumn(); // Source
                                ImGui.TextUnformatted(s.Source ?? "");

                                DrawHpColumn(s);

                                DrawStatusEffectsColumn(s);
                                break;
                            }
                        }
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawStatusEffectsColumn(CombatEvent e) {
            if (e.Snapshot.StatusEffects != null) {
                ImGui.TableNextColumn();
                foreach (var effect in e.Snapshot.StatusEffects) {
                    if (Service.DataManager.GetExcelSheet<Status>()?.GetRow(effect) is { } s) {
                        if (s.IsFcBuff) continue;
                        if (GetIconImage(s.Icon) is {} img) {
                            ImGui.SameLine();
                            ImGui.Image(img.ImGuiHandle, new Vector2(16, 16) * ImGuiHelpers.GlobalScale);
                            if (ImGui.IsItemHovered()) {
                                ImGui.BeginTooltip();
                                ImGui.TextUnformatted(s.Name);
                                ImGui.TextUnformatted(s.Description.DisplayedText());
                                ImGui.EndTooltip();
                            }
                        }
                    }
                }
            }
        }

        private void DrawHpColumn(CombatEvent e) {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, 0xFF058837);
            var hpFract = e.Snapshot.CurrentHp / (float?)e.Snapshot.MaxHp ?? 0;
            ImGui.ProgressBar(hpFract, new Vector2(-1, 0), $"{e.Snapshot.CurrentHp:N0}");
            ImGui.PopStyleColor();

            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();

            if (e.Snapshot.BarrierPercent is {} barrier) {
                var barrierFract = barrier / 100f;
                ImGui.GetWindowDrawList().PushClipRect(itemMin + new Vector2(0, (itemMax.Y - itemMin.Y) * 0.8f),
                    itemMin + new Vector2((itemMax.X - itemMin.X) * barrierFract, itemMax.Y), true);
                ImGui.GetWindowDrawList().AddRectFilled(itemMin, itemMax, 0xFF33FFFF, ImGui.GetStyle().FrameRounding);
                ImGui.GetWindowDrawList().PopClipRect();
            }
        }

        private void DrawTimeColumn(CombatEvent e, DateTime deathTime) {
            ImGui.TableNextColumn();
            ImGui.TextColored(ColorGrey, $"{(e.Snapshot.Time - deathTime).TotalSeconds:N1}s");
        }

        private TextureWrap? GetIconImage(ushort? icon) {
            if (icon is { } u) {
                if (textures.TryGetValue(u, out var tex))
                    return tex;
                if (Service.DataManager.GetImGuiTextureIcon(u) is { } t)
                    return textures[u] = t;
            }

            return null;
        }
    }
}