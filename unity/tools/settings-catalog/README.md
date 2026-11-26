# Catálogo de configurações (Godot → Unity)

O catálogo de configurações do Unity (`UnityClient/Assets/LifeVerse/UI/SettingsCatalog.Generated.cs`) é gerado a partir do catálogo do cliente Godot (`client/Scripts/Settings/SettingsCatalog*.cs`), para que os dois tenham os mesmos ids, rótulos, textos, padrões, limites e escolhas.

```powershell
python unity/tools/settings-catalog/extract.py client/Scripts/Settings $env:TEMP\catalog.json
python unity/tools/settings-catalog/generate.py $env:TEMP\catalog.json unity/UnityClient/Assets/LifeVerse/UI/SettingsCatalog.Generated.cs
```

O que cada configuração faz no Unity fica à mão em `SettingsCatalog.cs` (`InUnity`, extras e as do Godot que o Unity tem), `SettingsStore.cs` (ligação com as propriedades antigas) e `SettingsEffects.cs` (efeitos). O que não está em `InUnity` aparece desligado com o motivo.
