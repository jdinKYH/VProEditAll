using Cognex.VisionPro;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolGroup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace VProEditAll
{
    /// <summary>
    /// 261105@Claude [문서] 불러온 vpp 파일 하나 (불러오기 / 편집 대상 꺼내기 / 저장 / 정리)
    /// - QuickBuild(CogJobManager) : Job 을 골라 그 Job 의 VisionTool(ToolGroup) 을 편집, 저장은 JobManager 전체
    /// - Job(CogJob)               : VisionTool(ToolGroup) 을 편집, 저장은 CogJob 그대로
    /// - ToolGroup / ToolBlock     : 객체 자체를 편집/저장
    /// </summary>
    public class VppDocument : IDisposable
    {
        public string FilePath { get; private set; }
        public object Root { get; private set; }
        public VppKind Kind { get; private set; }

        private VppDocument() { }

        /// <summary>
        /// 파일 불러오기. 지원하지 않는 형식이면 NotSupportedException.
        /// </summary>
        public static VppDocument Load(string filePath)
        {
            object obj = CogSerializer.LoadObjectFromFile(filePath);
            VppKind kind = VppInspector.KindFromObject(obj);

            if (kind == VppKind.Unsupported || kind == VppKind.Unknown)
            {
                string typeName = obj == null ? "null" : obj.GetType().FullName;
                ReleaseObject(obj);
                throw new NotSupportedException($"지원하지 않는 형식입니다: {typeName}\n(QuickBuild / Job / ToolGroup / ToolBlock 파일만 편집할 수 있습니다.)");
            }

            VppDocument doc = new VppDocument { FilePath = filePath, Root = obj, Kind = kind };
            doc.StopRunning();  // 혹시 실행 상태로 저장된 경우 대비 (편집기에서는 실행하지 않음)
            return doc;
        }

        /// <summary>
        /// 편집 가능한 Job 개수 (QuickBuild: JobCount, Job: 1, 그 외: 0)
        /// </summary>
        public int JobCount
        {
            get
            {
                if (Kind == VppKind.QuickBuild) return ((CogJobManager)Root).JobCount;
                if (Kind == VppKind.Job) return 1;
                return 0;
            }
        }

        public string GetJobName(int index)
        {
            if (Kind == VppKind.QuickBuild) return ((CogJobManager)Root).Job(index).Name;
            if (Kind == VppKind.Job) return ((CogJob)Root).Name;
            return string.Empty;
        }

        /// <summary>
        /// 편집 화면에 연결할 대상 (ToolBlock 이면 CogToolBlock 객체가 그대로 반환됨)
        /// </summary>
        public CogToolGroup GetEditSubject(int jobIndex)
        {
            switch (Kind)
            {
                case VppKind.QuickBuild: return ((CogJobManager)Root).Job(jobIndex).VisionTool as CogToolGroup;
                case VppKind.Job: return ((CogJob)Root).VisionTool as CogToolGroup;
                case VppKind.ToolGroup:
                case VppKind.ToolBlock: return Root as CogToolGroup;
                default: return null;
            }
        }

        /// <summary>
        /// 261105@Claude [보완] 스크립트 컴파일 오류 목록
        /// - VisionPro 는 스크립트 컴파일 오류가 있어도 불러오기 예외를 내지 않고 ScriptError 에만 남김
        /// - 모든 Job 의 ToolGroup 과 그 안의 ToolGroup/ToolBlock 까지 재귀 검사
        /// </summary>
        public List<string> GetScriptErrors()
        {
            List<string> errors = new List<string>();
            int count = Math.Max(1, JobCount);
            for (int j = 0; j < count; j++)
            {
                string prefix = (Kind == VppKind.QuickBuild || Kind == VppKind.Job) ? GetJobName(j) : string.Empty;
                CollectScriptErrors(GetEditSubject(j), prefix, errors);
            }
            return errors;
        }

        private static void CollectScriptErrors(CogToolGroup group, string parentPath, List<string> errors)
        {
            if (group == null) return;
            string path = string.IsNullOrEmpty(parentPath) ? group.Name : parentPath + " > " + group.Name;

            // ScriptError 는 오류가 없어도 컴파일 로그를 담고 있음 (시험으로 확인)
            //   정상 : "-Referenced Assembly: ..." / "-Found possible Script Class: ..." (모두 '-' 로 시작)
            //   오류 : "Unable to load script code in Locate." / "Compile Error in Script: ..."
            // → '-' 로 시작하지 않는 줄이 있으면 오류로 판단
            List<string> errorLines = new List<string>();
            foreach (string raw in (group.ScriptError ?? string.Empty).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("-")) continue;
                errorLines.Add(line);
            }
            if (errorLines.Count > 0)
            {
                string summary = errorLines.Find(l => l.IndexOf("Compile Error", StringComparison.OrdinalIgnoreCase) >= 0) ?? errorLines[0];
                errors.Add(path + " : " + summary);
            }

            foreach (ICogTool tool in group.Tools)
            {
                if (tool is CogToolGroup sub) CollectScriptErrors(sub, path, errors);   // ToolBlock 포함
            }
        }

        /// <summary>
        /// QuickBuild 의 I/O 사용 설정 (QuickBuild 가 아니면 null)
        /// </summary>
        public bool? IOEnabled
        {
            get { return Kind == VppKind.QuickBuild ? (bool?)((CogJobManager)Root).IOEnable : null; }
        }

        /// <summary>
        /// 저장 (덮어쓰기 보호: 임시 파일에 먼저 저장 → 원본과 교체, 기존 원본은 .bak 으로 보관)
        /// ※ ExcludeDataBindings 는 툴 간 연결이 빠지므로 사용하지 않는다.
        /// </summary>
        /// <returns>만들어진 백업 파일 경로 (새 파일이면 null)</returns>
        public string Save(string filePath, CogSerializationOptionsConstants options)
        {
            StopRunning();   // 실행 중 저장 방지

            string fullPath = Path.GetFullPath(filePath);
            string tempPath = fullPath + ".saving.tmp";
            string backupPath = null;

            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);

                CogSerializer.SaveObjectToFile(Root, tempPath, typeof(BinaryFormatter), options);

                if (File.Exists(fullPath))
                {
                    backupPath = fullPath + ".bak";
                    File.Replace(tempPath, fullPath, backupPath, true);   // 기존 .bak 은 덮어씀
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }

                FilePath = fullPath;
                return backupPath;
            }
            finally
            {
                // 저장 실패 시 임시 파일 정리 (원본은 그대로 남아 있음)
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        /// <summary>
        /// Job/JobManager 실행 정지 (실행 중이 아니어도 안전하게 호출)
        /// </summary>
        public void StopRunning()
        {
            try
            {
                if (Kind == VppKind.QuickBuild) ((CogJobManager)Root).Stop();
                else if (Kind == VppKind.Job) ((CogJob)Root).Stop();
            }
            catch { }
        }

        /// <summary>
        /// 정리: 정지 → Shutdown(JobManager/Job) 또는 Dispose(ToolGroup/ToolBlock)
        /// ※ 편집 화면의 Subject 는 이 함수 호출 전에 먼저 null 로 끊어야 한다.
        /// </summary>
        public void Dispose()
        {
            StopRunning();
            ReleaseObject(Root);
            Root = null;
        }

        private static void ReleaseObject(object obj)
        {
            try
            {
                if (obj is CogJobManager jm) jm.Shutdown();
                else if (obj is CogJob job) job.Shutdown();
                else if (obj is IDisposable d) d.Dispose();
            }
            catch { }
        }
    }
}
