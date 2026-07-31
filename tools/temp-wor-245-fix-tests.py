from pathlib import Path

path = Path("src/BE/WorkslipApi/Workslip.Tests/Jobs/JobRejectionNotificationTests.cs")
text = path.read_text(encoding="utf-8")
old = "        using var services = new ServiceCollection().AddHybridCache().BuildServiceProvider();"
new = "        var serviceCollection = new ServiceCollection();\n        serviceCollection.AddHybridCache();\n        using var services = serviceCollection.BuildServiceProvider();"
if old in text:
    text = text.replace(old, new)
    path.write_text(text, encoding="utf-8")
