// This file is used to verify SK0001 fires on DateTime.UtcNow usage.
// Expected: SK0001 warning on the line below.
using System;

var now = DateTime.UtcNow;
Console.WriteLine(now);
