using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayGoblin
{
    /// <summary>
    /// Saves downscaled JPEG screenshots to &lt;runDir&gt;/frames as f&lt;index&gt;_t&lt;game seconds&gt;.jpg.
    /// Captures at a low rate normally and at a high rate for a few seconds after a finding, so the report can show
    /// what happened around it. Needs a rendering device: under -batchmode -nographics it switches itself off.
    /// </summary>
    public sealed class Recorder
    {
        readonly PlayGoblinRunOptions _options;
        readonly string _dir;
        float _nextCapture;
        float _burstUntil;
        int _index;
        bool _busy;
        float _busySince;
        int _generation;
        // End-of-frame capture includes UI overlays but never fires without a window (-batchmode). Then we render cameras.
        bool _useCameras;

        public bool Enabled { get; private set; }
        public string DisabledReason { get; private set; }
        public int FramesWritten => _index;
        public int Attempts { get; private set; }
        /// <summary>Time.frameCount of the latest capture, so frame-rate checks can ignore the frame it slowed down.</summary>
        public int LastCaptureFrame { get; private set; } = -100;
        public string LastProblem { get; private set; }

        public Recorder(PlayGoblinRunOptions options, string runDirectory)
        {
            _options = options;
            _dir = Path.Combine(runDirectory, "frames");
            if (!options.recordFrames) { DisabledReason = "disabled in options"; return; }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                DisabledReason = "no graphics device (running with -nographics)";
                return;
            }
            Enabled = true;
        }

        /// <summary>Capture at the higher rate for the next few seconds. Rates are per real second, so a sped-up game costs no more.</summary>
        public void Burst()
        {
            if (Enabled) _burstUntil = Time.unscaledTime + _options.burstSeconds;
        }

        /// <summary>Call every frame from the run's Update. Starts a capture when one is due.</summary>
        public void Tick(MonoBehaviour host, float gameTime)
        {
            if (!Enabled || _index >= _options.maxFrames) return;
            if (_busy)
            {
                if (Time.realtimeSinceStartup - _busySince < 0.5f) return;
                _generation++; // abandon the capture that never resumed
                _busy = false;
                _useCameras = true;
                LastProblem = "end-of-frame capture never fired; rendering cameras instead (UI overlays not included)";
            }
            var now = Time.unscaledTime;
            if (now < _nextCapture) return;
            LastCaptureFrame = Time.frameCount;
            var hz = now < _burstUntil ? _options.burstHz : _options.frameHz;
            _nextCapture = now + 1f / Mathf.Max(0.1f, hz);
            if (_useCameras) CaptureFromCameras(gameTime);
            else host.StartCoroutine(Capture(gameTime, _generation));
        }

        void CaptureFromCameras(float gameTime)
        {
            var cams = Camera.allCameras;
            if (cams.Length == 0) return;
            System.Array.Sort(cams, (a, b) => a.depth.CompareTo(b.depth));
            var first = cams[0];
            var aspect = first.pixelHeight > 0 ? first.pixelHeight / (float)first.pixelWidth : 9f / 16f;
            var width = _options.frameWidth;
            var height = Mathf.Max(1, Mathf.RoundToInt(width * aspect));
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                foreach (var cam in cams)
                {
                    var old = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = old;
                }
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                Write(tex, gameTime);
            }
            catch (System.Exception ex)
            {
                LastProblem = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) Object.Destroy(tex);
            }
        }

        void Write(Texture2D tex, float gameTime)
        {
            var bytes = tex.EncodeToJPG(_options.jpgQuality);
            Directory.CreateDirectory(_dir);
            var name = $"f{_index:D5}_t{gameTime.ToString("0.00", CultureInfo.InvariantCulture)}.jpg";
            File.WriteAllBytes(Path.Combine(_dir, name), bytes);
            _index++;
        }

        IEnumerator Capture(float gameTime, int generation)
        {
            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Attempts++;
            yield return new WaitForEndOfFrame();
            if (generation != _generation) yield break;
            Texture2D full = null, small = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full == null) throw new System.InvalidOperationException("ScreenCapture returned null");
                var tex = full;
                if (full.width > _options.frameWidth)
                {
                    small = Downscale(full, _options.frameWidth);
                    tex = small;
                }
                Write(tex, gameTime);
            }
            catch (System.Exception ex)
            {
                LastProblem = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (full != null) Object.Destroy(full);
                if (small != null) Object.Destroy(small);
                _busy = false;
            }
        }

        static Texture2D Downscale(Texture2D src, int width)
        {
            var height = Mathf.Max(1, Mathf.RoundToInt(src.height * (width / (float)src.width)));
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(width, height, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
