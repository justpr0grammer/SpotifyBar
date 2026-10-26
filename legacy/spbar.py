import sys
import os
import asyncio
import ctypes
import random
import threading
import time
import traceback

from PyQt5 import QtCore, QtGui, QtWidgets
from winrt.windows.media.control import (
    GlobalSystemMediaTransportControlsSessionManager as MTC,
)
from winrt.windows.storage.streams import (
    Buffer, DataReader, InputStreamOptions,
)

try:
    from ctypes import POINTER, cast
    from comtypes import CLSCTX_ALL
    from pycaw.pycaw import (
        AudioUtilities, IAudioEndpointVolume, IAudioMeterInformation,
    )
    HAS_PYCAW = True
except Exception:
    HAS_PYCAW = False

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
LOG_PATH = os.path.join(BASE_DIR, "spotify_bar_error.log")
MUTEX_NAME = "SpotifyBarSingleInstance"

W, H = 410, 436
COVER = 134
BARS = 14
OPACITY = 0.9
YELLOW = "#e4e600"

VK_LCONTROL = 0xA2
VK_RCONTROL = 0xA3
VK_M = 0x4D
HOTKEY_POLL_MS = 30

STYLE = f"""
QFrame#root {{ background:#12100a; border:2px solid {YELLOW}; }}
QLabel {{ color:{YELLOW}; background:transparent; }}
QPushButton {{ color:{YELLOW}; background:#1f1c10; border:2px solid {YELLOW}; border-radius:3px; }}
QPushButton:hover {{ background:#3d3819; }}
QPushButton:pressed {{ background:{YELLOW}; color:#14120c; }}
QToolButton {{ color:{YELLOW}; background:#1f1c10; border:2px solid {YELLOW}; border-radius:3px; }}
QToolButton:hover {{ background:#3d3819; }}
QSlider::groove:horizontal {{ background:#3d3819; height:4px; border-radius:2px; }}
QSlider::handle:horizontal {{ background:{YELLOW}; width:12px; margin:-5px 0; border-radius:6px; }}
QSlider::sub-page:horizontal {{ background:{YELLOW}; border-radius:2px; }}
QSlider::groove:vertical {{ background:#3d3819; width:4px; border-radius:2px; }}
QSlider::handle:vertical {{ background:{YELLOW}; height:5px; margin:0 -3px; border-radius:0px; }}
QSlider::sub-page:vertical {{ background:#3d3819; border-radius:2px; }}
QSlider::add-page:vertical {{ background:{YELLOW}; border-radius:2px; }}
QScrollArea {{ background:transparent; border:none; }}
QScrollBar:horizontal {{ background:#1f1c10; height:6px; }}
QScrollBar::handle:horizontal {{ background:{YELLOW}; border-radius:3px; min-width:20px; }}
QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal {{ width:0; }}
"""


# ---------- служебное ----------
def log_error(text):
    try:
        with open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(time.strftime("%Y-%m-%d %H:%M:%S ") + text + "\n")
    except Exception:
        pass


def message_box(text, title="Spotify Bar"):
    ctypes.windll.user32.MessageBoxW(None, text, title, 0x40)


def ensure_single_instance():
    kernel32 = ctypes.windll.kernel32
    ERROR_ALREADY_EXISTS = 183
    kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, ctypes.c_bool, ctypes.c_wchar_p]
    kernel32.CreateMutexW.restype = ctypes.c_void_p
    handle = kernel32.CreateMutexW(None, False, MUTEX_NAME)
    if not handle:
        raise ctypes.WinError()
    if kernel32.GetLastError() == ERROR_ALREADY_EXISTS:
        message_box("Spotify Bar уже запущен.\n"
                    "Закройте старый процесс pythonw.exe в Диспетчере задач.")
        sys.exit(0)
    return handle


# ---------- Spotify (Windows media API) ----------
async def get_track_info(known_key=None, known_thumb=None):
    mgr = await MTC.request_async()
    session = next((s for s in mgr.get_sessions()
                    if "spotify" in s.source_app_user_model_id.lower()), None)
    if session is None:
        return None

    props = await session.try_get_media_properties_async()
    key = (props.title, props.artist)
    playing = session.get_playback_info().playback_status == 4

    tl = session.get_timeline_properties()
    pos = tl.position.total_seconds()
    dur = max(0.0, (tl.end_time - tl.start_time).total_seconds())
    pos = max(0.0, pos - tl.start_time.total_seconds())

    thumb = known_thumb
    if key != known_key:
        thumb = None
        if props.thumbnail:
            stream = await props.thumbnail.open_read_async()
            buf = Buffer(stream.size)
            buf = await stream.read_async(buf, buf.capacity, InputStreamOptions.READ_AHEAD)
            reader = DataReader.from_buffer(buf)
            data = bytearray(reader.unconsumed_buffer_length)
            reader.read_bytes(data)
            thumb = bytes(data)
            reader.close()
            stream.close()

    return {
        "key": key,
        "title": props.title,
        "artist": props.artist,
        "playing": playing,
        "thumb": thumb,
        "pos": pos,
        "dur": dur,
    }


def media_action(method_name):
    async def go():
        try:
            mgr = await MTC.request_async()
            session = next((s for s in mgr.get_sessions()
                            if "spotify" in s.source_app_user_model_id.lower()), None)
            if session is not None:
                await getattr(session, method_name)()
        except Exception:
            log_error("media_action: " + traceback.format_exc())

    threading.Thread(target=lambda: asyncio.run(go()), daemon=True).start()


def fmt_time(sec):
    sec = max(0, int(sec))
    return f"{sec // 60}:{sec % 60:02d}"


# ---------- звук (pycaw) ----------
def get_spotify_volume():
    if not HAS_PYCAW:
        return None
    for s in AudioUtilities.GetAllSessions():
        if s.Process and s.Process.name().lower() == "spotify.exe":
            return s.SimpleAudioVolume
    return None


_icon_cache = {}


def app_icon(exe):
    if not exe:
        return None
    if exe not in _icon_cache:
        ico = QtWidgets.QFileIconProvider().icon(QtCore.QFileInfo(exe))
        _icon_cache[exe] = ico.pixmap(24, 24)
    return _icon_cache[exe]


def get_audio_sessions():
    result, seen = [], set()
    if not HAS_PYCAW:
        return result
    try:
        sessions = AudioUtilities.GetAllSessions()
    except Exception:
        log_error("GetAllSessions: " + traceback.format_exc())
        return result
    for s in sessions:
        try:
            proc = s.Process
            if proc is None or proc.pid in seen:
                continue
            seen.add(proc.pid)
            try:
                exe = proc.exe()
            except Exception:
                exe = ""
            result.append({"session": s, "name": proc.name(), "exe": exe, "pid": proc.pid})
        except Exception:
            log_error("session item: " + traceback.format_exc())
    return result


def make_endpoint(getter):
    if not HAS_PYCAW:
        return None
    try:
        dev = getter()
        if dev is None:
            log_error("endpoint: устройство не найдено")
            return None
        # В новых версиях pycaw устройство уже содержит EndpointVolume
        if hasattr(dev, "EndpointVolume"):
            return DirectEndpointAdapter(dev)
        return EndpointAdapter(dev)
    except Exception:
        log_error("endpoint: " + traceback.format_exc())
        return None

class SessionAdapter:
    def __init__(self, session):
        self.v = session.SimpleAudioVolume
        self.m = getattr(session, "AudioMeterInformation", None)
        if self.m is None:
            try:
                self.m = session._ctl.QueryInterface(IAudioMeterInformation)
            except Exception:
                self.m = None

    def get_volume(self):
        return self.v.GetMasterVolume()

    def set_volume(self, x):
        self.v.SetMasterVolume(x, None)

    def get_mute(self):
        return bool(self.v.GetMute())

    def set_mute(self, flag):
        self.v.SetMute(flag, None)

    def get_peak(self):
        return self.m.GetPeakValue() if self.m is not None else 0.0


class EndpointAdapter:
    def __init__(self, device):
        iface = device.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
        self.v = cast(iface, POINTER(IAudioEndpointVolume))
        meter = device.Activate(IAudioMeterInformation._iid_, CLSCTX_ALL, None)
        self.m = cast(meter, POINTER(IAudioMeterInformation))

    def get_volume(self):
        return self.v.GetMasterVolumeLevelScalar()

    def set_volume(self, x):
        self.v.SetMasterVolumeLevelScalar(x, None)

    def get_mute(self):
        return bool(self.v.GetMute())

    def set_mute(self, flag):
        self.v.SetMute(flag, None)

    def get_peak(self):
        return self.m.GetPeakValue()

class DirectEndpointAdapter:
    def __init__(self, device):
        self.v = device.EndpointVolume
        self.m = getattr(device, "AudioMeterInformation", None)
        if self.m is None:
            raw = getattr(device, "_dev", device)
            meter = raw.Activate(IAudioMeterInformation._iid_, CLSCTX_ALL, None)
            self.m = cast(meter, POINTER(IAudioMeterInformation))

    def get_volume(self):
        return self.v.GetMasterVolumeLevelScalar()

    def set_volume(self, x):
        self.v.SetMasterVolumeLevelScalar(x, None)

    def get_mute(self):
        return bool(self.v.GetMute())

    def set_mute(self, flag):
        self.v.SetMute(flag, None)

    def get_peak(self):
        return self.m.GetPeakValue()

# ---------- фоновый опрос Spotify ----------
class Poller(QtCore.QThread):
    changed = QtCore.pyqtSignal(object)

    def run(self):
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        known_key, known_thumb = None, None
        while not self.isInterruptionRequested():
            try:
                info = loop.run_until_complete(asyncio.wait_for(
                    get_track_info(known_key, known_thumb), timeout=5.0))
                if info:
                    known_key, known_thumb = info["key"], info["thumb"]
                else:
                    known_key, known_thumb = None, None
            except Exception:
                info = None
                known_key, known_thumb = None, None
            self.changed.emit(info)
            self.msleep(500)
        loop.close()


# ---------- Ctrl+M: опрос клавиш ----------
class HotkeyPoller(QtCore.QObject):
    fired = QtCore.pyqtSignal()

    def __init__(self):
        super().__init__()
        self.user32 = ctypes.windll.user32
        self.was_down = False
        self.timer = QtCore.QTimer(self)
        self.timer.timeout.connect(self._poll)
        self.timer.start(HOTKEY_POLL_MS)
        log_error("HotkeyPoller запущен")

    def _down(self, vk):
        return bool(self.user32.GetAsyncKeyState(vk) & 0x8000)

    def _poll(self):
        ctrl = self._down(VK_LCONTROL) or self._down(VK_RCONTROL)
        down = ctrl and self._down(VK_M)
        if down and not self.was_down:
            log_error("Ctrl+M нажато")
            self.fired.emit()
        self.was_down = down


# ---------- иконка в трее ----------
def make_tray_icon():
    pm = QtGui.QPixmap(32, 32)
    pm.fill(QtCore.Qt.transparent)
    p = QtGui.QPainter(pm)
    p.setRenderHint(QtGui.QPainter.Antialiasing)
    p.setPen(QtCore.Qt.NoPen)
    p.setBrush(QtGui.QColor(YELLOW))
    p.drawRoundedRect(0, 0, 32, 32, 6, 6)
    p.setPen(QtGui.QColor("#14120c"))
    p.setFont(QtGui.QFont("Segoe UI Symbol", 16))
    p.drawText(pm.rect(), QtCore.Qt.AlignCenter, "♪")
    p.end()
    return QtGui.QIcon(pm)


class Tray(QtWidgets.QSystemTrayIcon):
    def __init__(self, panel, app):
        super().__init__(make_tray_icon())
        self.panel = panel
        self.setToolTip("Spotify Bar (Ctrl+M)")

        menu = QtWidgets.QMenu()
        menu.addAction("Открыть / закрыть (Ctrl+M)", lambda: panel.hotkey_toggle())
        menu.addSeparator()
        menu.addAction("Выход", lambda: app.quit())
        self.menu = menu
        self.setContextMenu(menu)

        self.activated.connect(self._on_activated)
        self.show()

    def _on_activated(self, reason):
        if reason in (QtWidgets.QSystemTrayIcon.Trigger,
                      QtWidgets.QSystemTrayIcon.DoubleClick):
            self.panel.hotkey_toggle()


# ---------- виджеты ----------
class Bars(QtWidgets.QWidget):
    def __init__(self):
        super().__init__()
        self.setFixedHeight(35)
        self.levels = [0.0] * BARS
        self.targets = [0.0] * BARS
        self.playing = False

    def step(self):
        for i in range(BARS):
            if self.playing:
                if random.random() < 0.35:
                    self.targets[i] = random.uniform(0.15, 1.0)
            else:
                self.targets[i] = 0.0
            self.levels[i] += (self.targets[i] - self.levels[i]) * 0.4
        self.update()

    def paintEvent(self, e):
        p = QtGui.QPainter(self)
        p.setRenderHint(QtGui.QPainter.Antialiasing)
        p.setPen(QtCore.Qt.NoPen)
        p.setBrush(QtGui.QColor(YELLOW))
        bw, gap = 6, 4
        w, h = self.width(), self.height()
        x0 = w - (BARS * (bw + gap) - gap)
        for i, lv in enumerate(self.levels):
            hh = max(2, int(lv * h))
            p.drawRect(x0 + i * (bw + gap), h - hh, bw, hh)


class Channel(QtWidgets.QWidget):
    def __init__(self, adapter, icon_pm=None, icon_text="", tip=""):
        super().__init__()
        self.ad = adapter
        self.setFixedWidth(49)

        small = QtGui.QFont("Consolas", 8, QtGui.QFont.Bold)
        lay = QtWidgets.QVBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(4)

        ic = QtWidgets.QLabel()
        ic.setFixedSize(34, 34)
        ic.setAlignment(QtCore.Qt.AlignCenter)
        ic.setStyleSheet(f"border:2px solid {YELLOW}; background:#1d1a0d;")
        if icon_pm:
            ic.setPixmap(icon_pm)
        else:
            ic.setText(icon_text)
        ic.setToolTip(tip)

        self.pct = QtWidgets.QLabel("100%")
        self.pct.setFont(small)
        self.pct.setAlignment(QtCore.Qt.AlignCenter)

        self.slider = QtWidgets.QSlider(QtCore.Qt.Vertical)
        self.slider.setRange(0, 100)
        self.slider.setFixedHeight(120)
        self.slider.valueChanged.connect(self._on_change)

        self.peak = QtWidgets.QLabel("0%")
        self.peak.setFont(small)
        self.peak.setAlignment(QtCore.Qt.AlignCenter)

        self.mute = QtWidgets.QToolButton()
        self.mute.setFixedSize(30, 20)
        self.mute.setStyleSheet("border:none; background:transparent;")
        self.mute.setCursor(QtCore.Qt.PointingHandCursor)
        self.mute.clicked.connect(self._toggle_mute)

        lay.addWidget(ic, 0, QtCore.Qt.AlignHCenter)
        lay.addWidget(self.pct)
        lay.addWidget(self.slider, 0, QtCore.Qt.AlignHCenter)
        lay.addWidget(self.peak)
        lay.addWidget(self.mute, 0, QtCore.Qt.AlignHCenter)
        self.sync()

    def _on_change(self, v):
        try:
            self.ad.set_volume(v / 100.0)
        except Exception:
            log_error("set_volume: " + traceback.format_exc())
        self.pct.setText(f"{v}%")

    def _toggle_mute(self):
        try:
            self.ad.set_mute(not self.ad.get_mute())
        except Exception:
            log_error("set_mute: " + traceback.format_exc())
        self.sync()

    def sync(self):
        try:
            v = round(self.ad.get_volume() * 100)
            muted = self.ad.get_mute()
        except Exception:
            self.setEnabled(False)
            return
        if not self.slider.isSliderDown():
            self.slider.blockSignals(True)
            self.slider.setValue(v)
            self.slider.blockSignals(False)
        self.setEnabled(True)
        self.pct.setText(f"{v}%")
        self.mute.setText("🔇" if muted else "🔊")

    def sync_peak(self):
        try:
            p = round(self.ad.get_peak() * 100)
        except Exception:
            p = 0
        self.peak.setText(f"{p}%")


# ---------- главная панель ----------
class Panel(QtWidgets.QFrame):
    def __init__(self):
        super().__init__()
        self.setObjectName("root")
        self.setWindowFlags(
            QtCore.Qt.FramelessWindowHint
            | QtCore.Qt.WindowStaysOnTopHint
            | QtCore.Qt.Tool
        )
        self.setWindowOpacity(OPACITY)
        self.setFixedSize(W, H)
        screen = QtWidgets.QApplication.primaryScreen().availableGeometry()
        self.move(screen.x(), screen.y())
        self.setStyleSheet(STYLE)

        self.playing = False
        self.dur = 0.0
        self.pos_base = 0.0
        self.pos_at = time.monotonic()
        self._thumb_ref = None
        self.vol_iface = None
        self._mixer_keys = None
        self._mix_count = 0
        self.channels = []
        self._endpoints = None

        self.f_title = QtGui.QFont("Segoe UI", 10)
        self.f_artist = QtGui.QFont("Segoe UI", 9)
        self.f_time = QtGui.QFont("Segoe UI", 10)
        self.f_small = QtGui.QFont("Consolas", 8, QtGui.QFont.Bold)

        root = QtWidgets.QVBoxLayout(self)
        root.setContentsMargins(10, 10, 10, 10)
        root.setSpacing(5)

        # верх: обложка, название, бары, кнопки
        top = QtWidgets.QHBoxLayout()
        top.setSpacing(20)

        self.cover = QtWidgets.QLabel()
        self.cover.setFixedSize(COVER, COVER)
        self.cover.setAlignment(QtCore.Qt.AlignCenter)
        self.cover.setStyleSheet(f"border:2px solid {YELLOW}; background:#1d1a0d;")
        top.addWidget(self.cover, 0, QtCore.Qt.AlignTop)

        info = QtWidgets.QVBoxLayout()
        info.setSpacing(4)
        self.bars = Bars()
        info.addWidget(self.bars)

        self.lbl_title = QtWidgets.QLabel("")
        self.lbl_title.setFont(self.f_title)
        self.lbl_artist = QtWidgets.QLabel("")
        self.lbl_artist.setFont(self.f_artist)
        self.lbl_artist.setStyleSheet("color:#e6e6e6; background:transparent;")
        self.lbl_time = QtWidgets.QLabel("")
        self.lbl_time.setFont(self.f_time)
        self.lbl_time.setFixedWidth(170)
        info.addWidget(self.lbl_title)
        info.addWidget(self.lbl_artist)
        info.addWidget(self.lbl_time)
        info.addStretch()

        btns = QtWidgets.QHBoxLayout()
        btns.setSpacing(8)
        self.prev_btn = self._btn("⏮", lambda: media_action("try_skip_previous_async"))
        self.play_btn = self._btn("▶", lambda: media_action("try_toggle_play_pause_async"))
        self.next_btn = self._btn("⏭", lambda: media_action("try_skip_next_async"))
        for b in (self.prev_btn, self.play_btn, self.next_btn):
            btns.addWidget(b)
        btns.addStretch()
        info.addLayout(btns)

        top.addLayout(info, 1)
        root.addLayout(top)

        # громкость Spotify
        vol_row = QtWidgets.QHBoxLayout()
        vl = QtWidgets.QLabel("VOL")
        vl.setFont(self.f_small)
        self.vol = QtWidgets.QSlider(QtCore.Qt.Horizontal)
        self.vol.setRange(0, 100)
        self.vol.valueChanged.connect(self._on_spotify_vol)
        self.vol.setEnabled(HAS_PYCAW)
        vol_row.addSpacing(COVER + 20)
        vol_row.addWidget(vl)
        vol_row.addWidget(self.vol, 1)
        root.addLayout(vol_row)

        line = QtWidgets.QFrame()
        line.setFixedHeight(1)
        line.setStyleSheet(f"background:{YELLOW};")
        root.addWidget(line)

        # мікшер
        mix_title = QtWidgets.QLabel("МІКШЕР")
        mix_title.setFont(self.f_small)
        # Compact mixer matches the reference without a heading.

        self.scroll = QtWidgets.QScrollArea()
        self.scroll.setWidgetResizable(True)
        self.scroll.setFrameShape(QtWidgets.QFrame.NoFrame)
        self.scroll.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.scroll.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAsNeeded)
        self.scroll.viewport().setStyleSheet("background:transparent;")

        self.mix_host = QtWidgets.QWidget()
        self.mix_lay = QtWidgets.QHBoxLayout(self.mix_host)
        self.mix_lay.setContentsMargins(0, 0, 0, 0)
        self.mix_lay.setSpacing(9)
        self.mix_lay.setAlignment(QtCore.Qt.AlignLeft)
        self.scroll.setWidget(self.mix_host)
        root.addWidget(self.scroll, 1)

        # таймери (працюють тільки коли панель видно)
        self.tick_timer = QtCore.QTimer(self)
        self.tick_timer.timeout.connect(self._tick)
        self.tick_timer.start(100)

        self.vol_timer = QtCore.QTimer(self)
        self.vol_timer.timeout.connect(self._sync_spotify_vol)
        self.vol_timer.start(1000)

        self.mix_timer = QtCore.QTimer(self)
        self.mix_timer.timeout.connect(self._sync_mixer)
        self.mix_timer.start(300)

        self._drag_offset = None
        self.on_update(None)
        self.hide()

    def mousePressEvent(self, event):
        if event.button() == QtCore.Qt.LeftButton:
            self._drag_offset = event.globalPos() - self.frameGeometry().topLeft()

    def mouseMoveEvent(self, event):
        if self._drag_offset is not None and event.buttons() & QtCore.Qt.LeftButton:
            self.move(event.globalPos() - self._drag_offset)

    def mouseReleaseEvent(self, event):
        self._drag_offset = None

    def _btn(self, text, slot):
        b = QtWidgets.QPushButton(text)
        b.setFixedSize(36, 32)
        b.setCursor(QtCore.Qt.PointingHandCursor)
        b.setFont(QtGui.QFont("Segoe UI Symbol", 11))
        b.clicked.connect(slot)
        return b

    # ---------- показ / приховування ----------
    def hotkey_toggle(self):
        if self.isVisible():
            self.hide()
            return
        self.show()
        self.raise_()
        self.activateWindow()
        try:
            self._refresh_mixer(force=True)
            self._sync_spotify_vol()
        except Exception:
            log_error("open panel: " + traceback.format_exc())

    def keyPressEvent(self, e):
        if e.key() == QtCore.Qt.Key_Escape:
            self.hide()

    # ---------- дані треку ----------
    def on_update(self, info):
        if not info:
            self.playing = False
            self.dur = 0.0
            self.bars.playing = False
            self.cover.clear()
            self._thumb_ref = None
            self.pos_base = 0.0
            self.lbl_title.setText("Spotify")
            self.lbl_artist.setText("Немає активного треку")
            self.lbl_time.setText("0:00 / 0:00")
            self.play_btn.setText("▶")
            return

        self.playing = info["playing"]
        self.bars.playing = self.playing
        self.dur = info["dur"]
        self.pos_base = info["pos"]
        self.pos_at = time.monotonic()
        self.play_btn.setText("⏸" if self.playing else "▶")

        self.lbl_title.setText(
            QtGui.QFontMetrics(self.f_title).elidedText(info["title"], QtCore.Qt.ElideRight, self.lbl_title.width()))
        self.lbl_artist.setText(
            QtGui.QFontMetrics(self.f_artist).elidedText(info["artist"], QtCore.Qt.ElideRight, self.lbl_title.width()))

        self.lbl_time.setText(f"{fmt_time(self.elapsed())} / {fmt_time(self.dur)}")
        if not info["thumb"]:
            self.cover.clear()
            self._thumb_ref = None
        if info["thumb"] and info["thumb"] != self._thumb_ref:
            self._thumb_ref = info["thumb"]
            pix = QtGui.QPixmap()
            if not pix.loadFromData(info["thumb"]):
                self.cover.clear()
                return
            sc = pix.scaled(COVER, COVER,
                            QtCore.Qt.KeepAspectRatioByExpanding,
                            QtCore.Qt.SmoothTransformation)
            x = (sc.width() - COVER) // 2
            y = (sc.height() - COVER) // 2
            self.cover.setPixmap(sc.copy(x, y, COVER, COVER))

    def elapsed(self):
        pos = self.pos_base
        if self.playing:
            pos += time.monotonic() - self.pos_at
        if self.dur > 0:
            pos = min(pos, self.dur)
        return pos

    def _tick(self):
        if not self.isVisible():
            return
        self.bars.step()
        text = fmt_time(self.elapsed())
        if self.dur > 0:
            text += f" / {fmt_time(self.dur)}"
        if text != self.lbl_time.text():
            self.lbl_time.setText(text)

    # ---------- гучність Spotify ----------
    def _sync_spotify_vol(self):
        if not HAS_PYCAW or not self.isVisible():
            return
        try:
            self.vol_iface = get_spotify_volume()
            if self.vol_iface is None:
                self.vol.setEnabled(False)
                return
            self.vol.setEnabled(True)
            value = round(self.vol_iface.GetMasterVolume() * 100)
            if not self.vol.isSliderDown():
                self.vol.blockSignals(True)
                self.vol.setValue(value)
                self.vol.blockSignals(False)
        except Exception:
            self.vol_iface = None

    def _on_spotify_vol(self, value):
        if self.vol_iface is None:
            return
        try:
            self.vol_iface.SetMasterVolume(value / 100.0, None)
        except Exception:
            self.vol_iface = None

    # ---------- мікшер ----------
    def _get_endpoints(self):
        if self._endpoints is None or any(x is None for x in self._endpoints):
            spk = make_endpoint(lambda: AudioUtilities.GetSpeakers())
            mic = make_endpoint(lambda: AudioUtilities.GetMicrophone())
            self._endpoints = (spk, mic)
        return self._endpoints

    def _refresh_mixer(self, force=False):
        if not HAS_PYCAW:
            return
        sess = get_audio_sessions()
        keys = tuple(sorted(s["pid"] for s in sess))
        if not force and keys == self._mixer_keys:
            return
        self._mixer_keys = keys

        while self.mix_lay.count():
            item = self.mix_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        self.channels = []

        spk, mic = self._get_endpoints()
        log_error(f"mixer: spk={'ok' if spk else 'None'}, mic={'ok' if mic else 'None'}")
        if spk:
            ch = Channel(spk, icon_text="🔈", tip="Загальна гучність")
            self.mix_lay.addWidget(ch)
            self.channels.append(ch)
        if mic:
            ch = Channel(mic, icon_text="🎤", tip="Мікрофон")
            self.mix_lay.addWidget(ch)
            self.channels.append(ch)

        for s in sess:
            try:
                ch = Channel(SessionAdapter(s["session"]),
                             icon_pm=app_icon(s["exe"]), tip=s["name"])
            except Exception:
                log_error("session adapter: " + traceback.format_exc())
                continue
            self.mix_lay.addWidget(ch)
            self.channels.append(ch)

        if not self.channels:
            empty = QtWidgets.QLabel("немає активних програм")
            empty.setFont(self.f_small)
            self.mix_lay.addWidget(empty)

    def _sync_mixer(self):
        if not self.isVisible() or not HAS_PYCAW:
            return
        self._mix_count += 1
        if self._mix_count % 10 == 0:
            self._refresh_mixer()
        for ch in self.channels:
            ch.sync_peak()
            if self._mix_count % 3 == 0:
                ch.sync()


def main():
    _mutex = ensure_single_instance()  # noqa: F841
    log_error("Запуск Spotify Bar")

    app = QtWidgets.QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)

    if not QtWidgets.QSystemTrayIcon.isSystemTrayAvailable():
        log_error("Системный трей недоступен")

    panel = Panel()

    poller = Poller()
    poller.changed.connect(panel.on_update)
    poller.start()

    hotkey = HotkeyPoller()
    hotkey.fired.connect(panel.hotkey_toggle)

    tray = Tray(panel, app)  # noqa: F841

    def shutdown():
        poller.requestInterruption()
        poller.wait()
        tray.hide()
        ctypes.windll.kernel32.CloseHandle(ctypes.c_void_p(_mutex))

    app.aboutToQuit.connect(shutdown)
    panel.hotkey_toggle()
    sys.exit(app.exec_())


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        log_error("main: " + traceback.format_exc())
        raise
