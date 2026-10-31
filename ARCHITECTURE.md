# Spike Court 2.5D – Arsitektur Proyek

Game voli mobile 2.5D bertema Haikyuu!! (Hinata = spiker, Kageyama = setter AI).
Lapangan, bola, dan fisika berjalan di ruang 3D. Karakter adalah sprite 2D billboard.

> Catatan IP: nama dan karakter Haikyuu!! milik pemegang hak ciptanya. Untuk prototipe/fan project, gunakan sprite buatan sendiri (bukan hasil rip). Kalau game akan dirilis publik, butuh lisensi resmi atau ganti ke karakter orisinal.

---

## 1. Rekomendasi engine: **Unity** (2022.3 LTS atau Unity 6, URP)

| Kebutuhan | Unity | Godot 4 |
|---|---|---|
| Fisika bola 3D (kontinu, spin, prediksi) | PhysX stabil, `Rigidbody` Continuous Dynamic | Jolt/GodotPhysics bagus, tapi kontrol CCD dan tooling debug lebih terbatas |
| Kamera sinematik (ganti sudut saat serangan) | **Cinemachine** siap pakai (blend, dolly, impulse shake) | Harus dibuat manual |
| Sprite 2D di dunia 3D | `SpriteRenderer` + billboard, **2D Animation/Sprite Library**, Spine/Aseprite importer | Sprite3D, cukup baik |
| VFX Burst oranye/hitam | **VFX Graph / Shader Graph / URP Volume** (color grading, vignette, ink-splash) | Shader manual |
| Android | Pipeline matang (IL2CPP ARM64, Addressables, Play Games, Ads/IAP SDK) | Bagus dan ringan, ekosistem SDK lebih kecil |
| Bahasa | C# (sesuai skrip yang diminta) | GDScript/C# |

**Alasan utama:** Cinemachine untuk sinematik "Freak Quick", ekosistem Android/monetisasi, dan Shader Graph untuk efek kilatan. Pilih Godot hanya jika prioritas utamamu ukuran APK kecil dan proyek 100% gratis tanpa lisensi.

**Paket:** Cinemachine, Input System (On-Screen Stick/Button), TextMeshPro, 2D Animation, Post Processing (URP Volume).

---

## 2. Struktur folder

```
Assets/
  Scripts/
    Core/        GameTypes.cs  GameDirector.cs  RallyStateMachine.cs  ScoreManager.cs
    Gameplay/    BallPhysics.cs  PlayerController.cs  SetterAI.cs  BurstMeter.cs
                 OpponentAI.cs  BlockSystem.cs  ReceiveRing.cs
    Visual/      SpriteBillboard.cs  CinematicCamera.cs  BurstVFX.cs  CourtShadow.cs
    UI/          HudController.cs  VirtualControls.cs  TimingRingUI.cs
    Audio/       AudioManager.cs
  Art/           Sprites/  Animations/  VFX/  Materials/
  Prefabs/       Ball  Hinata  Kageyama  Opponent  Court
  Scenes/        Boot  Match
```

Yang sudah ada di folder ini (siap dipakai): `GameTypes`, `BallPhysics`, `PlayerController`, `SetterAI`, `BurstMeter`, `SpriteBillboard`. Sisanya adalah rancangan.

## 3. Tanggung jawab skrip

| Skrip | Tugas |
|---|---|
| **GameTypes** | `Team`, `HitGrade`, `SpikeResult`, konstanta `Court`, interface `ISetter` |
| **BallPhysics** | Gravitasi kustom, drag, Magnus, prediksi pendaratan/kontak, `LaunchByTime/Apex` tepat sasaran, hit-stop, event lantai/net |
| **PlayerController** | Gerak Hinata, jendela Receive (ring), lari decoy, lompat, spike, aktivasi Burst |
| **SetterAI** | Kageyama ke titik umpan, menahan bola, melempar Quick Set presisi |
| **BurstMeter** | Bar Semangat Juang, penuh = Freak Quick siap |
| **SpriteBillboard** | Sprite selalu sejajar kamera |
| *ReceiveRing (rancangan)* | Ring dunia di lantai. Skala = `sisaWaktu / total` dari event `OnReceiveWindow` |
| *BlockSystem (rancangan)* | Menyelesaikan spike: kalau `unblockable` abaikan blok lawan |
| *OpponentAI (rancangan)* | Receive/set/spike lawan, memanggil `BallPhysics.LaunchBy*` |
| *CinematicCamera (rancangan)* | Berlangganan `OnAttackSequenceStarted`, blend Cinemachine (Gameplay -> Cinematic -> Gameplay) |
| *BurstVFX (rancangan)* | Berlangganan `OnBurstFired`: flash oranye/hitam, garis kecepatan, slow-mo 0.15 dtk |
| *GameDirector (rancangan)* | Alur rally, skor, serve, reset posisi |

## 4. Alur game (core loop)

```
SERVE (lawan) ─► bola menyeberang
   │
FASE BERTAHAN   BallPhysics.TryGetContact() ──► PlayerController.OnReceiveWindow ──► ReceiveRing (mengecil)
   │            Hinata digerakkan ke titik jatuh, tekan OnAction() saat ring pas
   ▼
RECEIVE sukses  (Perfect/Good) ──► bola LaunchByApex ke tangan Kageyama ──► BurstMeter.Add
   │
   ├─ event OnAttackSequenceStarted ──► CinematicCamera (sudut dramatis)
   ▼
FASE MENYERANG  Hinata lari (kurva decoy) ─► melompat ─► Kageyama ScheduleQuickSet() (tahan 0.12 dtk, lempar)
   │            Bola tiba di titik kontak = apex lompatan. Pemain tekan OnAction() (Spike)
   ▼
SPIKE           Perfect / Good / Miss ─► LaunchByTime ke target lawan
   │            Burst armed? ──► SpikeResult.unblockable = true + OnBurstFired (VFX)
   ▼
RESOLUSI        BlockSystem / OpponentAI / OnFloorHit ─► ScoreManager ─► rally berikutnya
```

State `PlayerController`: `Idle ⇄ Moving → AttackRun → Airborne → Recovering → Idle`.

## 5. Setup scene (Match)

- **Sumbu:** X = panjang lapangan (net di x=0), Z = lebar, Y = tinggi. Tim pemain di x < 0.
- **Lantai:** Plane 18x9 m, tag `Floor`. **Net:** Box collider tipis, tag `Net`.
- **Bola:** Sphere radius 0.105 + `BallPhysics` + Physic Material (Bounciness 0.65, Bounce Combine Maximum). Tambah Quad kecil sebagai `shadowBlob`.
- **Hinata/Kageyama:** objek kosong (logika, XZ) dengan anak `Sprite` (SpriteRenderer + `SpriteBillboard` + Animator). Hinata punya `PlayerController` + Rigidbody (kinematik otomatis).
- **Kamera:** Perspektif, miring 20–25° dari sisi lapangan. Buat 3 Cinemachine Virtual Camera: *Gameplay*, *Receive* (zoom lembut), *AttackCinematic*.
- **Animator Hinata:** parameter `Speed` (float), `Airborne` (bool), trigger `Receive`, `Dive`, `Whiff`, `Run`, `Jump`, `Spike`.
- **Input mobile:** Input System On-Screen Stick -> `PlayerController.OnMove`, tombol ACTION -> `OnAction`, tombol BURST -> `OnBurst`.
- **Burst VFX:** Volume Global dengan Color Adjustments (oranye/hitam, kontras tinggi), Particle System garis kecepatan, Image UI "ink splash" yang di-fade 0.4 dtk.

## 6. Parameter yang perlu disetel (feel)

| Parameter | Lokasi | Default | Efek |
|---|---|---|---|
| `gravityScale` | BallPhysics | 1.15 | Bola lebih melayang / cepat |
| `perfectWindow`, `goodWindow` | PlayerController | 0.07 / 0.16 dtk | Kesulitan timing |
| `spikeEarlyWindow`, `spikeLateWindow` | PlayerController | 0.30 / 0.12 dtk | Toleransi tombol Spike |
| `jumpApex`, `jumpGravity` | PlayerController | 1.6 m / 24 | Lompatan tinggi khas Hinata |
| `setFlight`, `scatter` | PlayerController, SetterAI | 0.45 dtk / 0.03 m | Kecepatan & presisi quick set |

## 7. Catatan teknis

- Unity 6 mengganti `Rigidbody.velocity` menjadi `linearVelocity`. `BallPhysics` menangani keduanya lewat `#if UNITY_6000_0_OR_NEWER`.
- Prediksi memakai integrasi Euler `Step = 0.02`, samakan dengan *Fixed Timestep* proyek.
- Spike memakai *hit-stop* kecil agar tombol yang telat sedikit masih terhitung. Ini keputusan desain, bukan bug.
- Hitung tabrakan bola dengan blocker lawan di `BlockSystem`, bukan di `BallPhysics`, supaya aturan `unblockable` tetap satu tempat.

## 8. Langkah berikutnya

1. Buat scene Match, pasang prefab, lalu tes receive tanpa lawan (lempar bola dari script debug).
2. Buat `ReceiveRing` + `HudController` (bar Burst, skor).
3. Tambah `OpponentAI` + `BlockSystem`, lalu loop skor.
4. `CinematicCamera` + `BurstVFX`.
5. Pasang sprite dan animasi final, lalu build Android (IL2CPP, ARM64).
