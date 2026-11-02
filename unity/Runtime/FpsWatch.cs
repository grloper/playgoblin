namespace PlayGoblin
{
    /// <summary>Reports a stretch of slow frames: average frame time above a budget for a minimum duration.</summary>
    public sealed class FpsWatch
    {
        public float BudgetMs;
        public float MinSeconds = 1f;
        /// <summary>Quiet period after an event so one long stutter is reported once.</summary>
        public float CooldownSeconds = 5f;

        float _slowSeconds;
        float _slowFrames;
        float _slowMsSum;
        float _cooldownUntil;

        public struct Drop
        {
            public float AverageMs;
            public float Seconds;
        }

        /// <summary>
        /// Feed one frame. <paramref name="ignore"/> marks frames that are skipped (right after load, or the frame
        /// PlayGoblin itself captured a screenshot). Returns true when a drop should be reported.
        /// </summary>
        public bool Feed(float unscaledDeltaSeconds, float gameTime, bool ignore, out Drop drop)
        {
            drop = default;
            if (BudgetMs <= 0f) return false;
            var ms = unscaledDeltaSeconds * 1000f;
            if (ignore) return false; // skipped: neither counts as slow nor breaks a run of slow frames
            if (ms <= BudgetMs)
            {
                _slowSeconds = _slowFrames = _slowMsSum = 0f;
                return false;
            }
            _slowSeconds += unscaledDeltaSeconds;
            _slowFrames++;
            _slowMsSum += ms;
            if (_slowSeconds < MinSeconds || gameTime < _cooldownUntil) return false;
            drop = new Drop { AverageMs = _slowMsSum / _slowFrames, Seconds = _slowSeconds };
            _cooldownUntil = gameTime + CooldownSeconds;
            _slowSeconds = _slowFrames = _slowMsSum = 0f;
            return true;
        }
    }
}
