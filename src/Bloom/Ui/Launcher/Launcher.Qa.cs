using System.Collections.Generic;
using Bloom.Search;

namespace Bloom.Ui;

/// Narrow instrumentation for the isolated responsiveness probe; uses the production refresh and renderer.
internal sealed partial class Launcher
{
    internal void QaRender(List<Section> values)
    {
        sections = values;
        Render(values, null);
    }

    internal int QaRowCount => rows.Count;
    internal string? QaLastRowId => rows.Count == 0 ? null : rows[^1].Item?.Id;
    internal string QaQueryText => box.Text;
    internal bool QaHasResult(string id) => rows.Exists(row => row.Item?.Id == id);
    internal void QaQuery(string text) => box.Text = text;
}
