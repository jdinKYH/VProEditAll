using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VProEditAll
{
    // =================================================================================================
    // 261105@Claude [형식 판정] vpp 파일 형식 구분
    // =================================================================================================
    public enum VppKind
    {
        Unknown,        // 판정 불가 (불러온 뒤 객체로 다시 판정)
        QuickBuild,     // CogJobManager (QuickBuild 전체)
        Job,            // CogJob (QuickBuild Job 단독 저장)
        ToolGroup,      // CogToolGroup
        ToolBlock,      // CogToolBlock
        Unsupported     // 툴 하나만 저장한 파일 등 (예: CogAcqFifoTool)
    }

    /// <summary>
    /// 261105@Claude [형식 판정] 파일을 바이트로만 읽은 결과 (VisionPro로 불러오지 않음 → 카메라/I/O 연결 없음)
    /// </summary>
    public class VppFileInfo
    {
        public string RootTypeName = string.Empty;  // 파일 최상위 객체 형식 이름 (예: Cognex.VisionPro.QuickBuild.CogJobManager)
        public VppKind Kind = VppKind.Unknown;
        public bool IsBinary;                       // BinaryFormatter 형식 여부 (false 이면 XML/SOAP 등)
        public bool HasAcqFifo;                     // 영상 취득(AcqFifo) 설정 포함
        public bool HasGigE;                        // GigE 카메라 설정 포함
        public bool HasIOSettings;                  // I/O·통신(Discrete, TCP/IP) 설정 포함
    }

    /// <summary>
    /// 261105@Claude [형식 판정] vpp 파일 형식/위험 요소(카메라, I/O) 판정
    /// - 파일 앞부분에서 최상위 객체 형식 이름을 찾는다.
    /// - 파일 전체에서 AcqFifo / GigE / I/O 설정 문자열을 찾는다.
    /// </summary>
    public static class VppInspector
    {
        private const int HeadLength = 8192;

        public static VppFileInfo Inspect(string filePath)
        {
            VppFileInfo info = new VppFileInfo();
            byte[] data = File.ReadAllBytes(filePath);
            if (data.Length == 0) return info;

            // BinaryFormatter 파일은 첫 바이트가 0 (SerializedStreamHeader)
            info.IsBinary = data[0] == 0;

            string head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, HeadLength));
            Match m;
            if (info.IsBinary)
            {
                // 바이너리: 처음 나오는 Cognex 클래스 이름이 최상위 객체
                m = Regex.Match(head, @"Cognex\.VisionPro[\w\.]*\.Cog\w+");
                if (m.Success) info.RootTypeName = m.Value;
            }
            else
            {
                // XML(SOAP): <SOAP-ENV:Body> 다음 첫 요소가 최상위 객체 (예: <a1:CogJobManager ...>)
                int bodyIndex = head.IndexOf("SOAP-ENV:Body", StringComparison.Ordinal);
                if (bodyIndex >= 0)
                {
                    m = new Regex(@"<a\d+:(Cog\w+)").Match(head, bodyIndex);
                    if (m.Success) info.RootTypeName = m.Groups[1].Value;
                }
            }

            info.Kind = KindFromTypeName(info.RootTypeName);
            info.HasAcqFifo = ContainsAscii(data, "CogAcqFifo");
            info.HasGigE = ContainsAscii(data, "GigE");
            info.HasIOSettings = ContainsAscii(data, "CogIODiscreteInfoList") || ContainsAscii(data, "CogIOTCPIPInfoList");
            return info;
        }

        /// <summary>
        /// 형식 이름 → VppKind (전체 이름/짧은 이름 모두 허용)
        /// </summary>
        public static VppKind KindFromTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return VppKind.Unknown;
            string shortName = typeName.Substring(typeName.LastIndexOf('.') + 1);
            switch (shortName)
            {
                case "CogJobManager": return VppKind.QuickBuild;
                case "CogJob": return VppKind.Job;
                case "CogToolBlock": return VppKind.ToolBlock;
                case "CogToolGroup": return VppKind.ToolGroup;
                default: return VppKind.Unsupported;
            }
        }

        /// <summary>
        /// 불러온 객체 → VppKind
        /// ※ CogToolBlock 은 CogToolGroup 을 상속하므로 반드시 ToolBlock 을 먼저 검사한다.
        /// </summary>
        public static VppKind KindFromObject(object obj)
        {
            if (obj == null) return VppKind.Unknown;
            if (obj is CogJobManager) return VppKind.QuickBuild;
            if (obj is CogJob) return VppKind.Job;
            if (obj is CogToolBlock) return VppKind.ToolBlock;     // ToolGroup 보다 먼저
            if (obj is CogToolGroup) return VppKind.ToolGroup;
            return VppKind.Unsupported;
        }

        public static string DisplayName(VppKind kind)
        {
            switch (kind)
            {
                case VppKind.QuickBuild: return "QuickBuild";
                case VppKind.Job: return "Job";
                case VppKind.ToolGroup: return "ToolGroup";
                case VppKind.ToolBlock: return "ToolBlock";
                case VppKind.Unsupported: return "미지원";
                default: return "알 수 없음";
            }
        }

        /// <summary>
        /// 바이트 배열에서 ASCII 문자열 검색
        /// </summary>
        private static bool ContainsAscii(byte[] data, string text)
        {
            byte[] pattern = Encoding.ASCII.GetBytes(text);
            int last = data.Length - pattern.Length;
            int i = 0;
            while (i <= last)
            {
                i = Array.IndexOf(data, pattern[0], i, last - i + 1);
                if (i < 0) return false;
                int k = 1;
                while (k < pattern.Length && data[i + k] == pattern[k]) k++;
                if (k == pattern.Length) return true;
                i++;
            }
            return false;
        }
    }
}
