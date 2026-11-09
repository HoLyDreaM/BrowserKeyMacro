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
- Global tetikleyici: düşük seviye klavye kancası (`WH_KEYBOARD_LL`), yalnız **Başlat** sonrası aktif; tuş atama sırasında kapalı
- Tuş atama: yalnız `KeyCaptureTextBox.OnKeyDown` → `e.KeyCode`; tek yazma yolu `SetCapturedKey(Keys, source)` (her yazma `macro-debug.log`'a `SET KEY` + stack trace)
- Tetikleyici açılır listeden seçilir (harf/rakam yok — oyun içinde güvenilir algılanmıyor)
- **Sürekli döngü**: işaretliyken Başlat sonrası sıra Durdur'a kadar tekrarlanır; kapalıyken yalnız tetik tuşuyla bir kez çalışır
- Varsayılan tuşlar arası gecikme: 500 ms
- **Tuş Yakalama Tanı Testi**: 1. tuş kutusunda basılan tuşun ham `KeyCode` / `KeyValue` değerini gösterir
- Başlıkta sürüm + çalışan EXE yolu (eski EXE ile karışmasın)
- Hedef zaten ön plandaysa BringToFront/Alt atlanır; gönderim process eşleşmesi + transient retry
- En fazla 4 ana tuş + isteğe bağlı ara tuş
- Ara tuş doluysa: `Ara → Key1 → Ara → Key2 → Ara → Key3 → Ara → Key4`
- `SendInput` VK+scan (Test = hotkey aynı yol); kendi enjeksiyonu `dwExtraInfo` ile Raw’dan ayrılır
- Caps Lock açık/kapalı aynı tetik (sabit VK; char/ToUnicode yok)
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
| `KeyboardInputService` | `SendInput` VK+scan KEYDOWN/KEYUP + ForceKeyUp (ExtraInfo) |
| `GlobalHotkeyService` | Poll-armed birincil tetikleyici (+ Raw Input yardımcı) |
| `AppSettingsService` | `settings.ini` |
| `MacroDebugLog` | `macro-debug.log` |

### Tetikleyici mimarisi (1.22)

1. **Başlat** → `SetNotifyWindow(MainForm.Handle)` + Raw Input (`RIDEV_INPUTSINK`) + poll thread.
2. Tetik birincil: `GetAsyncKeyState` poll + **armed** kenar; debounce armed CAS’ten önce; Raw Input yardımcı.
3. `BeginInvoke` → `ExecuteTriggerSameAsButton` → Test ile aynı `SendInput` yolu.
4. Gönderim: process eşleşmesi; **zaten FG ise BringToFront yok**; değilse öne al. `ForceKeyUp` (yalnız down) + sıra (Intermediate dahil).
5. `canContinue`: process-only; transient miss’te 1 kısa retry; abort = process değişimi / pencere kapalı.
6. `SuppressInput` sırasında yeni tetik yok; poll sahte UP re-arm yok; Raw fiziksel UP + suppress-off reconcile.
7. Kendi `SendInput`/`ForceKeyUp` Raw yankısı `dwExtraInfo` ile yok sayılır.
8. Caps Lock: sabit VK eşlemesi — harf tetik/gönderim layout/Caps’ten bağımsız.
9. Yanlış tuşlar durum kartına yazılmaz (yalnız `macro-debug.log`).

Tuş gönderiminde Chrome/Edge/Firefox vb. için ayrı `if` / sabit process adı yoktur.

## Davranış

- Her tetik / test: tek sıra; gönderim sırasında ikinci tetik kuyruğa alınır (bitince bir kez).
- Boş tuş alanları atlanır.
- Ara tuş doluysa sıra: Ara → Tuş1 → Ara → Tuş2 → … (ör. Z + 1 + 3 → `Z → 1 → Z → 3`).
- SendKeys / PostMessage / WM_CHAR / DOM / JavaScript kullanılmaz.
- `SendInput` başarısızsa Win32 kodu durum kartında gösterilir.
- Durum kartı gönderilen sırayı gösterir (`Makro tamamlandı: Z → 1 → Z → 3…`).

## Sürüm

- **1.38.0** — Varsayılan gecikme 500 ms; Sürekli döngü kutusu (Durdur'a kadar tekrar)
- **1.37.0** — Tetikleyici açılır listeden (harf/rakam yok: F1–F12, Esc, Ctrl, Shift, Alt, oklar, Insert/Home/End/PgUp/PgDn …)
- **1.36.0** — Oyun içi harf/rakam tetik: enjekte edilmiş tuşlar da tetikler (yalnız kendi SendInput'umuz filtrelenir)
- **1.35.0** — Tek yazma yolu `SetCapturedKey` + SET KEY/stack trace logu, tanı testi, başlıkta EXE yolu
- **1.34.0** — KeyCaptureTextBox: ilk KeyDown; atamada hook tamamen kapalı
- **1.33.0** — Tuş atama: yalnız Keys/KeyCode; INI sabit enum id
- **1.32.0** — Durum tüm tuşlar; harf/rakam scan eşleşmesi (VkKeyScanEx)
- **1.31.0** — Algılama: ToUnicodeEx (aktif düzen); durum yalnız tetik tuşu
- **1.30.0** — Algılama: LL kanca + F klavye scan (F tuşu Q diye okunmaz)
- **1.29.0** — Basılan tuş yine durum çubuğunda; harf/rakam Raw VKey + UI poll
- **1.28.0** — Tuş spam kaldırıldı; sade rising-edge poll tetik
- **1.27.0** — Prime (önce bırak) + FG gate + rising-edge; durum kuyruk sapması yok
- **1.26.0** — Harf/sayı Raw VK düzeltmesi (MakeCode→VK)
- **1.25.0** — Tab/Ctrl/Shift/Alt atama; durum spam kesildi; HWND öne alma (Alt yok)
- **1.24.0** — Durum çubuğunda basılan tuş gösterimi
- **1.22.0** — BringToFront no-op (zaten FG); debounce/armed; ExtraInfo Raw echo; canContinue retry; Caps Lock VK notu
- **1.21.0** — Poll-armed tetik (birincil); LL kaldırıldı; VK+scan gönderim; process eşleşme
- **1.20.0** — HWND eşleşme; scancode SendInput; ForceKeyUp; LL-hook + poll tetik
- **1.19.0** — Tetik kenar kilidi düzeltildi (KEYUP/suppress); poll-assist; debounce 180ms
- **1.18.0** — Yanlış-tuş UI spam kaldırıldı; gönderim sırasında SuppressInput; sıra durum kartında
- **1.17.0** — Raw Input birincil tetikleyici; Assets runtime bağımlılığı kaldırıldı
- **1.16.0** — Tetik kuyruğu / KEY spy durum kartı
- **1.3.0** — Tarayıcı-agnostik gönderim yolu netleştirildi
- **1.2.0** — Process eşleşmesi, scan-code, test, log
- **1.1.x / 1.0.0** — Ara tuş ve ilk sürüm
