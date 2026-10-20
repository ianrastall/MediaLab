import os

directory = r"d:\GitHub\MediaLab\LyricsLabV2\UI"
for root, dirs, files in os.walk(directory):
    for file in files:
        if file.endswith(".cs") or file.endswith(".xaml"):
            path = os.path.join(root, file)
            with open(path, "r", encoding="utf-8") as f:
                content = f.read()
            
            # Simple replacements to fix namespaces
            content = content.replace("namespace LyricsLab.UI", "namespace LyricsLabV2.UI")
            content = content.replace("clr-namespace:LyricsLab.UI", "clr-namespace:LyricsLabV2.UI")
            content = content.replace("x:Class=\"LyricsLab.UI", "x:Class=\"LyricsLabV2.UI")
            content = content.replace("using LyricsLab.UI", "using LyricsLabV2.UI")
            
            with open(path, "w", encoding="utf-8") as f:
                f.write(content)

print("Namespace replacement complete.")
