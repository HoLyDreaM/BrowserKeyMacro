# Browser Key Macro

Listeden seçilen tarayıcı penceresi ön plandayken tetikleyici tuşa basıldığında, tanımlanan klavye sırasını Win32 `SendInput` (scan code) ile gönderen .NET 8 Windows Forms uygulaması.

**Proje kökü:** `BrowserKeyMacro/` (`d:\SoftWare\BrowserKeyMacro`)

## Marka / ikon

- Kaynak: `BrowserKeyMacro/Assets/BrowserKeyMacro.ico` — `ApplicationIcon` (exe gömülü; çıktıda `Assets\` klasörü yok)
- Kaynak: `BrowserKeyMacro/Assets/icon.png` → çıktıda exe yanında `icon.png`
- Runtime logo: `AppContext.BaseDirectory\icon.png`; form/tepsi ikonu exe’den `ExtractAssociatedIcon`

## Özellikler

- Açık tarayıcı pencerelerini listeler (Chrome, Edge, Firefox, Brave, Opera, Vivaldi ve benzerleri — keşif listesi)
- Tuş gönderimi **tarayıcıya özel değil**: seçilen pencerenin işlem adı + başlığı dinamik kullanılır
- Aktif kontrol: ön plan işlem adı == seçilen işlem adı (HWND zorunlu değil)
- Global tetikleyici: **Raw Input** (`RegisterRawInputDevices` keyboard + `RIDEV_INPUTSINK` → ana form `WM_INPUT`) — oyun/tarayıcı odaktayken, makro diğer ekranda olsa da
- Yedek yol: yalnız Raw Input kaydı başarısız veya `WM_INPUT` gelmezse `GetAsyncKeyState` poll
- En fazla 4 ana tuş + isteğe bağlı ara tuş
- Ara tuş doluysa: `Ara → Key1 → Ara → Key2 → Ara → Key3 → Ara → Key4`
- Yalnız `SendInput` (`KEYEVENTF_SCANCODE`, KEYDOWN + KEYUP)
- **Tuş Dizisini Test Et** (tek sefer)
- `macro-debug.log` ve durum kartı geri bildirimi
- Ayarlar: `settings.ini`

## Gereksinimler

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (derleme)
- .NET 8 Desktop Runtime (çalıştırma)

**UIPI / bütünlük seviyesi:** Windows, daha düşük bütünlükteki süreçlerin yükseltilmiş (yönetici) pencerelere tuş enjekte etmesini engeller. Makro ile hedef tarayıcı aynı yükseltilme seviyesinde olmalıdır; Chrome/Edge yönetici olarak açıldıysa makro da yönetici olarak çalıştırılmalıdır.

## Derleme

```bash
cd d:\SoftWare\BrowserKeyMacro
dotnet build BrowserKeyMacro.sln -c Release
```

## Çalıştırma

```bash
cd d:\SoftWare\BrowserKeyMacro
dotnet run --project BrowserKeyMacro\BrowserKeyMacro.csproj -c Release
```

Çıktı: `BrowserKeyMacro\bin\Release\net8.0-windows\BrowserKeyMacro.exe`

## Kullanım

1. **Yenile** → listeden hedef pencereyi seçin (`Başlık | işlemadı`).
2. Tetikleyici, ara tuş (opsiyonel) ve tuş sırasını ayarlayın.
3. Gecikme (ms) ve isteğe bağlı **Tarayıcıyı öne getir**.
4. **Başlat** → pencere açık kalır; durum **Makro aktif**.
5. Tetikleyiciye basın — önce hedef öne alınır, sonra tuşlar gider (Test ile aynı mantık).
6. **Başlat** → durum `Tetikleyici aktif: Q` (veya seçilen tuş).
7. Tetikleyici tuş → `Global tetikleyici alındı: …` → **TETİKLE** ile aynı sıra bir kez → `Makro tamamlandı`.
8. Kapat (X) uygulamayı tamamen sonlandırır.

### Genel tarayıcı test adımları

Aynı adımlar Chrome, Edge, Firefox, Brave, Opera, Vivaldi ve listelenen diğer işlemler için geçerlidir (özel dal yok).

1. Hedef tarayıcıyı normal kullanıcıyla açın.
2. Uygulamada **Yenile** → ilgili satırı seçin.
3. Tuş sırasını ayarlayın.
4. Seçilen tarayıcı penceresine tıklayıp öne getirin.
5. **Tuş Dizisini Test Et**’e basın.
6. Durum kartında her tuş için `X gönderildi (SendInput: …)` görünmeli; sonda **Test tamamlandı**.
7. Sorun olursa exe yanındaki `macro-debug.log` dosyasına bakın:
   - `selectedProc=` seçiminiz
   - `fgProc=` ön plan işlem adı (eşit olmalı)
   - `SendInput` DOWN/UP ve Win32 kodları
8. Test başarılıysa **Başlat** + tetikleyici kullanın.

Seçilen işlem açık değilse veya ön planda değilse tuş gönderilmez; neden durum kartında yazılır.

## Ayar dosyası

```ini
[General]
SelectedBrowserProcess=
SelectedBrowserTitle=
DelayMs=300
BringBrowserToFront=true

[Keys]
Trigger=Q
IntermediateKey=
Key1=1
Key2=2
Key3=3
Key4=4
```

`SelectedBrowserProcess` / `SelectedBrowserTitle` kullanıcı seçiminden kaydedilir; sabit tarayıcı adına bağlı değildir.

## Mimari (genel yol)

| Bileşen | Rol |
|---------|-----|
| `BrowserWindowService` | Pencere keşfi (liste); seçim yenileme |
| `ForegroundBrowserService` | Ön plan işlem adı ↔ seçilen işlem adı |
| `KeyboardInputService` | `SendInput` scan-code KEYDOWN/KEYUP |
| `GlobalHotkeyService` | Raw Input birincil tetikleyici (+ poll yedek) |
| `AppSettingsService` | `settings.ini` |
| `MacroDebugLog` | `macro-debug.log` |

### Tetikleyici mimarisi (1.17+)

1. **Başlat** → `SetNotifyWindow(MainForm.Handle)` + `RegisterRawInputDevices` (UsagePage=0x01, Usage=0x06, `RIDEV_INPUTSINK`).
2. Ana form `WndProc` → `ProcessMessage` → `WM_INPUT` edge-detect → `Trigger FIRE (rawinput)` → `BeginInvoke` → `ExecuteTriggerSameAsButton` → `SendInput`.
3. Kayıt fail veya tuş basılmasına rağmen `WM_INPUT` yoksa → `Trigger FIRE (poll-fallback)`.
4. `WH_KEYBOARD_LL` / `RegisterHotKey` birincil yol olarak kullanılmaz. Interception/kernel sürücü yok.

Tuş gönderiminde Chrome/Edge/Firefox vb. için ayrı `if` / sabit process adı yoktur.

## Davranış

- Her tetik / test: tek sıra; gönderim sırasında ikinci tetik kuyruğa alınır (bitince bir kez).
- Boş tuş alanları atlanır.
- SendKeys / PostMessage / WM_CHAR / DOM / JavaScript kullanılmaz.
- `SendInput` başarısızsa Win32 kodu durum kartında gösterilir.

## Sürüm

- **1.17.0** — Raw Input birincil tetikleyici; Assets runtime bağımlılığı kaldırıldı
- **1.16.0** — Tetik kuyruğu / KEY spy durum kartı
- **1.3.0** — Tarayıcı-agnostik gönderim yolu netleştirildi
- **1.2.0** — Process eşleşmesi, scan-code, test, log
- **1.1.x / 1.0.0** — Ara tuş ve ilk sürüm
