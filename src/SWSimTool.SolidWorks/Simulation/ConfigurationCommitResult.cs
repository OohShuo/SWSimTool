using System;
using System.Collections.Generic;

namespace SWSimTool.Simulation {
    // Persistence success is irrevocable here; presentation failures are warnings.
    public sealed class ConfigurationCommitResult {
        public bool Committed { get; private set; } = true;
        public bool RefreshSucceeded => warnings.Count == 0;
        public string BackupPath { get; internal set; }
        readonly List<string> warnings = new List<string>();
        public string Message => RefreshSucceeded ? "配置已保存。" : "配置已提交，但刷新失败。请关闭旧页面后重新进入；不要重复保存。\n" + string.Join("\n", warnings);
        internal void Run(Action action) { try { action(); } catch (Exception e) { warnings.Add(e.Message); } }
        internal void Merge(ConfigurationCommitResult other) { warnings.AddRange(other.warnings); }
    }
}
