using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;

namespace CK.Metrics;

public class InstrumentConfiguration
{
    bool _enabled;

    public InstrumentConfiguration()
    {
    }

    /// <summary>
    /// Gets or sets whether the instrument is enabled.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}



