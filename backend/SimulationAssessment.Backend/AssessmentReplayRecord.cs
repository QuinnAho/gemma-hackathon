using System.Collections.Generic;
using GemmaHackathon.SimulationFramework;

namespace GemmaHackathon.Backend.AssessmentCli
{
    public sealed class AssessmentReplayRecord
    {
        public string SessionId = string.Empty;
        public string SessionState = string.Empty;
        public string Phase = string.Empty;
        public string SourceSessionDirectory = string.Empty;
        public AuditSessionRecord SessionRecord = new AuditSessionRecord();
        public AssessmentArtifacts Assessment = new AssessmentArtifacts();
        public List<AuditEvent> Timeline = new List<AuditEvent>();
    }
}
