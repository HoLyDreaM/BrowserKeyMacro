# Değişiklik Günlüğü

## 1.18.0

- Durum kartı spam'i kaldırıldı: yanlış tuşlar artık "Algılanan: Q / W / …" diye sayılmaz (yalnız `macro-debug.log`).
- Makro gönderimi sırasında Raw Input / poll bastırılır (`SuppressInput`) — kendi gönderdiğimiz Z/1/3 tetik/spy karıştırmasın.
- Durum metni gönderilen sırayı gösterir: `Gönderiliyor: Z → 1 → Z → 3…` / `Makro tamamlandı: …`.
- Log kanıtı (önceki oturum): `F` ateşlenince `SendInput` Z,1,Z,3,Z,1,Z,3 tamamlanmıştı; kullanıcıda görünen "3 basılmadı" çoğunlukla UI spam + gönderim sırasında ekstra fiziksel tuşlardı.

## 1.17.0

- Tetikleyici mimarisi: birincil yol **Raw Input** (`RegisterRawInputDevices` keyboard UsagePage=0x01 Usage=0x06 + `RIDEV_INPUTSINK`, `hwndTarget` = ana form) → `WM_INPUT` → edge-detect → `BeginInvoke` → `ExecuteTriggerSameAsButton` → `SendInput`.
- Yedek: yalnız kayıt başarısız veya kanıtlı `WM_INPUT` yokluğunda `GetAsyncKeyState` poll (`Trigger FIRE (poll-fallback)`).
- Kaldırılan eşzamanlı birincil yollar: `WH_KEYBOARD_LL`, `RegisterHotKey` birincil, “hepsini dene” dedektörleri.
- Log: `Raw Input register OK/FAIL`, `Fallback poll armed…`, `Trigger FIRE (rawinput|poll-fallback)`.
- Assets: runtime `Assets\` klasörü yok; `icon.png` exe yanında; ico yalnız `ApplicationIcon` / exe gömülü.
- Başlat’ta `SetUiNotify` tekrar spy callback (`OnKeySpyMismatch`) ile bağlanır; yanlış tuş uyarısı kaybolmaz.
- Sürüm metinleri **1.17.0**.

## 1.16.0

- Log kanıtı: oyundayken `F` algılanıyordu ama `gönderim sürüyor` diye yok sayılıyordu → **kuyruk**.
- Yanlış tuş (Q/Z vb.) durum kartında: `Algılanan: Q — tetikleyici 'F' değil`.
- Gidiş yolu: tetik → kuyruk/çalıştır → Test ile aynı SendInput.

## 1.15.0

- Uygulama ikonu: `Assets/BrowserKeyMacro.ico` (çok boyutlu, şeffaf).
- Form başlığı, görev çubuğu, `.exe` ve NotifyIcon aynı ikonu kullanır.
- Üst başlıkta `icon.png` logo (koyu tema / cyan-yeşil uyumlu).
- Runtime yol: `AppContext.BaseDirectory\Assets\` (publish uyumlu).

## 1.14.0

- Overlay kaldırıldı — oyundayken klavye tetikleyici.
- 5ms busy-poll `GetAsyncKeyState` + LL-hook; UI’ye `BeginInvoke`.
- Hedef zaten öndeyse BringToFront atlanır (oyundayken hızlı gönderim).
- KEY spy: basılan tuşlar log’a yazılır.

## 1.13.0

- Overlay denemesi (kullanıcı reddetti — oyunda tıklanamıyor).

## 1.12.0

- Tetikleyici üç yol (tek thread): LL-hook + Raw Input INPUTSINK + GetAsyncKeyState poll 15ms.
- Oyun 1. ekranda odaktayken de algılama; TETİKLE ile aynı gönderim; bip sesi.
- `bin` yeniden üretildi.

## 1.11.0

- Çift monitör / oyun odaktayken tetikleyici: `RegisterHotKey` gelmediği için `WH_KEYBOARD_LL` + WM_HOTKEY post.
- 2. ekranda Test çalışırken 1. ekranda oyun odaklı tetikleyici de aynı TETİKLE yolunu kullanır.
- Poll yok; basılı tutunca tek tetik; SendInput yolu değişmedi.

## 1.10.0

- Global tetikleyici yeniden yalnız `RegisterHotKey` + `MOD_NOREPEAT` + `WM_HOTKEY` (LL-hook yok).
- Normal masaüstü/tarayıcı (Chrome, Edge, Firefox, Not Defteri) odaktayken makro penceresi odakta olmasa da tetiklenir.
- Başarısız tetiklemede log: RegisterHotKey durumu, WM_HOTKEY, aktif HWND/işlem adı.
- Hedef ön planda değilse net hata; döngü yok.

## 1.9.0

- `RegisterHotKey` oyundayken (Edge/Raw Input) gelmediği için tetikleyici `WH_KEYBOARD_LL` + ayrı mesaj thread.
- Tetikte forma `WM_HOTKEY` post → aynı durum metinleri + `ExecuteTriggerSameAsButton` (TETİKLE).
- Poll / Raw Input yok. SendInput / tarayıcı yolu değişmedi.
- Log aynı: `RegisterHotKey success/fail`, `WM_HOTKEY received`, `Macro execution started/completed`.

## 1.8.0

- Global tetikleyici yalnız `RegisterHotKey` + `MOD_NOREPEAT` + `WM_HOTKEY`.
- LL-hook, Raw Input ve poll timer kaldırıldı.
- `WM_HOTKEY` → durum `Global tetikleyici alındı: …` → test ile aynı ortak metot.
- Log: `RegisterHotKey success/fail`, `WM_HOTKEY received`, `Macro execution started/completed`.

## 1.7.0

- Tetikleyici tuş ve mavi **TETİKLE** aynı `ExecuteTriggerSameAsButton` yolunu kullanır.
- Hook KEYDOWN log + debounce; edge-gate kilidi kaldırıldı.
- Oyunda tuş geldiği log ile doğrulandı; tuş basılınca butonla birebir gönderim.

## 1.6.0

- Tetikleyici ayrı STA thread: `WH_KEYBOARD_LL` + Raw Input `INPUTSINK` (message-only HWND) + `GetAsyncKeyState` poll.
- Oyunda klavye yutulursa tetikleyici olarak **X1 / X2 / orta mouse** desteklenir (kutuya mouse ile basarak ata).
- `RegisterHotKey` kaldırıldı (oyunda F gelmiyordu).
- Başlık `v1.6.0`.

## 1.5.0

- Tetikleyici: `RegisterRawInputDevices` + `RIDEV_INPUTSINK` (tarayıcı/oyun öndeyken WM_INPUT).
- LL-hook kaldırıldı (oyundayken tuşu yutmuyordu / zincirde kayboluyordu).
- Başlık `v1.5.0`.

## 1.4.1

- STA hook thread denemesi.

## 1.4.0

- TETİKLE butonu, canlı durum, poll + RegisterHotKey + LL-hook.

## 1.3.6

- Form.components `_triggerTimer` denemesi.

## 1.3.5

- GetAsyncKeyState denemesi (LL hook yerine).

## 1.3.4

- Tetikleyici PostMessage denemesi (LL hook .NET’te anahtar almıyordu).

## 1.3.3

- Başlat artık tepsiye/arkaya kaçmaz; pencere görünür kalır (Test ile aynı kullanım).
- Kapat (X) uygulamayı tamamen kapatır; arka planda bırakmaz.

## 1.3.2

- Global tetikleyici `RegisterHotKey` yerine `WH_KEYBOARD_LL` (AHK tarzı) — Win32 1409 çakışması giderildi.
- Tetikleyici basılı tutulunca tek tetikleme; gönderim sırasında suppress.

## 1.3.1

- AutoHotkey benzeri akış: her tetik/testte önce WinActivate, sonra SendInput.
- Başlatınca uygulama tepsiye küçülür (odak makroda kalmaz).
- WinActivate güçlendirildi (AllowSetForegroundWindow, Alt unlock, retry).
- SendInput: VK+Scan (AHK) + gerekirse saf scan-code yedek.

## 1.3.0

- Tuş gönderimi ve aktif kontrol tamamen seçilen `ProcessName` / başlığa bağlandı (tarayıcı adına özel dal yok).
- “Kapalı” tespiti: seçilen işlem adına görünür pencere var mı (`HasVisibleWindowForProcess`).
- Varsayılan ayarlarda sabit `chrome` kaldırıldı; README genel tarayıcı test adımlarına çekildi.

## 1.2.0

- Aktif tarayıcı kontrolü HWND yerine **process name** öncelikli.
- `SendInput` yeniden yazıldı: `KEYEVENTF_SCANCODE`, KEYDOWN + bekleme + KEYUP, dönüş/Win32 hata kontrolü.
- `macro-debug.log` ile HWND / foreground / SendInput izleme.
- **Tuş Dizisini Test Et** butonu eklendi.
- Sıra bitince durum: **Makro tamamlandı** / **Test tamamlandı**.
- README’ye Edge test adımları eklendi.

## 1.1.1

- Seçilen pencere HWND’si korunur (başlık değişince yanlış tarayıcıya kayma giderildi).
- “Tarayıcıyı öne getir” daha güvenilir hale getirildi; varsayılan açık.
- `SendInput` sanal tuş + scan code ile güçlendirildi (tarayıcı/oyun uyumu).
- Odak yokken durum mesajı netleştirildi: `… tuş gönderilmedi`.

## 1.1.0

- **Ara tuş** alanı eklendi: doluysa her ana tuştan hemen önce gönderilir (`E → 1 → E → 2 …`).
- `settings.ini` içine `IntermediateKey` anahtarı eklendi (yükleme/kaydetme).
- Gönderim sırasında hotkey bastırması ara tuş = tetikleyici durumunda da geçerlidir.
- README kullanım ve INI örnekleri güncellendi.

## 1.0.0

- İlk sürüm: tarayıcı seçimi, global hotkey, en fazla 4 tuş, `settings.ini`, koyu tema UI.
