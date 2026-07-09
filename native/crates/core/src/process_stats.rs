//! Lightweight, low-frequency process/GPU resource sampling shared by the compositor's own
//! tracing diagnostics and the periodic `Event::CoreStats` sent to the C# host for its status
//! bar. Every function here is meant to be called every few seconds at most — none of this is
//! cheap enough (or needs to be) for a per-frame hot path.

use std::time::{Duration, Instant};

use windows::core::Interface;
use windows::Win32::Graphics::Dxgi::{
    CreateDXGIFactory1, IDXGIAdapter3, IDXGIFactory1, DXGI_MEMORY_SEGMENT_GROUP_LOCAL,
    DXGI_QUERY_VIDEO_MEMORY_INFO,
};

/// Point-in-time CPU/memory snapshot — cheap enough (two syscalls, no allocation) to call every
/// couple of seconds with no measurable overhead of its own. `cpu_percent` is normalized by
/// logical core count (Task Manager's convention: 100% means "fully saturating every core"),
/// not raw summed-across-cores CPU time — a process using N cores flat-out on a machine with N
/// logical cores reads as 100%, not N*100%. (An earlier version reported the raw unnormalized
/// figure, which could read as e.g. "120%" on a 6-core machine at just 20% Task-Manager-style
/// load — confusing for a quick-glance status bar, even though it was arithmetically correct.)
pub struct ProcessStats {
    pub working_set_mb: f64,
    pub cpu_percent: f64,
}

/// Tracks the previous sample's CPU time so `sample()` can report a delta-based percentage
/// instead of a lifetime average (which would go stale/meaningless hours into a long session).
pub struct ProcessStatsSampler {
    last_instant: Instant,
    last_cpu_time: Duration,
    /// Cached once — logical core count never changes at runtime, no reason to re-query it.
    logical_cores: f64,
}

impl ProcessStatsSampler {
    pub fn new() -> Self {
        let logical_cores = std::thread::available_parallelism().map_or(1, |n| n.get()) as f64;
        Self { last_instant: Instant::now(), last_cpu_time: Duration::ZERO, logical_cores }
    }

    pub fn sample(&mut self) -> ProcessStats {
        use windows::Win32::Foundation::FILETIME;
        use windows::Win32::System::ProcessStatus::{K32GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS};
        use windows::Win32::System::Threading::{GetCurrentProcess, GetProcessTimes};

        let handle = unsafe { GetCurrentProcess() };

        let mut working_set_mb = 0.0;
        let mut counters = PROCESS_MEMORY_COUNTERS::default();
        let size = std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32;
        if unsafe { K32GetProcessMemoryInfo(handle, &mut counters, size) }.as_bool() {
            working_set_mb = counters.WorkingSetSize as f64 / (1024.0 * 1024.0);
        }

        let filetime_to_duration = |ft: FILETIME| {
            let ticks = ((ft.dwHighDateTime as u64) << 32) | ft.dwLowDateTime as u64;
            Duration::from_nanos(ticks * 100) // FILETIME is in 100ns ticks
        };

        let (mut creation, mut exit, mut kernel, mut user) =
            (FILETIME::default(), FILETIME::default(), FILETIME::default(), FILETIME::default());
        let mut cpu_percent = 0.0;
        if unsafe { GetProcessTimes(handle, &mut creation, &mut exit, &mut kernel, &mut user) }.is_ok() {
            let cpu_time = filetime_to_duration(kernel) + filetime_to_duration(user);
            let now = Instant::now();
            let wall_elapsed = now.duration_since(self.last_instant);
            let cpu_elapsed = cpu_time.saturating_sub(self.last_cpu_time);
            if wall_elapsed.as_secs_f64() > 0.0 {
                cpu_percent = cpu_elapsed.as_secs_f64() / wall_elapsed.as_secs_f64() * 100.0 / self.logical_cores;
            }
            self.last_instant = now;
            self.last_cpu_time = cpu_time;
        }

        ProcessStats { working_set_mb, cpu_percent }
    }
}

/// Resolves the primary GPU adapter for VRAM queries only — deliberately independent of whatever
/// capture sessions/D3D11 devices come and go (capture.rs's own device is per-session and may not
/// exist at all while idle), so the status bar's VRAM figures don't depend on a capture being
/// active. Created once and cached by the caller; cheap to call but no reason to repeat it every
/// sample.
pub fn create_dxgi_adapter() -> Option<IDXGIAdapter3> {
    unsafe {
        let factory: IDXGIFactory1 = CreateDXGIFactory1().ok()?;
        let adapter = factory.EnumAdapters(0).ok()?;
        adapter.cast::<IDXGIAdapter3>().ok()
    }
}

/// (used_mb, total_mb) for the adapter's local (dedicated VRAM) memory segment. `None` if the
/// query itself fails — e.g. a driver that doesn't support the newer QueryVideoMemoryInfo API.
pub fn sample_vram(adapter: &IDXGIAdapter3) -> Option<(f32, f32)> {
    let mut info = DXGI_QUERY_VIDEO_MEMORY_INFO::default();
    unsafe { adapter.QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &mut info).ok()? };
    let used_mb = info.CurrentUsage as f32 / (1024.0 * 1024.0);
    let total_mb = info.Budget as f32 / (1024.0 * 1024.0);
    Some((used_mb, total_mb))
}
