using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace VProEditAll
{
    /// <summary>
    /// 261105@Claude VisionPro vpp 편집기 (QuickBuild / Job / ToolGroup / ToolBlock)
    /// - VProEditorGroup1 을 바탕으로 새로 만든 프로젝트
    /// - 장비 PC 사용 전제: 불러오기 전 카메라/I/O 경고, 편집기 안에서 실행 버튼 숨김, 종료 시 자원 정리
    /// </summary>
    public partial class MainForm : Form
    {
        private VppDocument _doc;           // 현재 불러온 파일
        private Control _editor;            // 현재 편집 화면 (CogToolGroupEditV2 또는 CogToolBlockEditV2)
        private bool _fillingJobs;          // Job 콤보 채우는 중 (선택 이벤트 무시)
        private bool _cameraFileLoaded;     // 카메라(AcqFifo/GigE) 설정이 있는 파일을 한 번이라도 불러왔는지 (종료 시 카메라 해제 여부)

        // ----- 상단 1줄: 파일 -----
        private Panel topPanel;
        private Button btnLoad;
        private TextBox txtFileName;
        private Button btnSaveAs;
        private Button btnSave;

        // ----- 상단 2줄: 형식 / Job / 저장 옵션 -----
        private Label lblKind;
        private Label lblJob;
        private ComboBox cboJob;
        private Label lblSaveOption;
        private ComboBox cboSaveOption;

        private Panel panelEditorContainer;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
        private ToolTip _toolTip;

        public MainForm()
        {
            InitializeComponentCustom();
        }

        #region UI 구성

        private void InitializeComponentCustom()
        {
            this.Text = "VisionPro vpp Editor (QuickBuild / Job / ToolGroup / ToolBlock)";
            this.Width = 1200;
            this.Height = 800;
            this.StartPosition = FormStartPosition.CenterScreen;

            _toolTip = new ToolTip();

            topPanel = new Panel() { Dock = DockStyle.Top, Height = 84 };

            // 1줄: [파일 로드] [파일 이름 .........] [다른 이름으로 저장] [저장]
            // 261105@Claude [버튼 입체감] Button → Button3D (그라데이션 + 그림자, 높이는 그림자 2px 포함 32)
            btnLoad = new Button3D() { Text = "vpp 파일 로드", Left = 12, Top = 9, Width = 180, Height = 32 };
            btnLoad.Click += BtnLoad_Click;

            txtFileName = new TextBox() { Left = 200, Top = 15, Width = 600, ReadOnly = true, TabStop = false };

            btnSaveAs = new Button3D() { Text = "다른 이름으로 저장", Top = 9, Width = 140, Height = 32, Enabled = false };
            btnSaveAs.Click += BtnSaveAs_Click;

            btnSave = new Button3D() { Text = "저장", Top = 9, Width = 100, Height = 32, Enabled = false };
            btnSave.Click += BtnSave_Click;

            // 2줄: 형식: [QuickBuild]  Job: [콤보]  저장 옵션: [콤보]
            lblKind = new Label() { Left = 12, Top = 50, Width = 180, Height = 24, TextAlign = ContentAlignment.MiddleLeft, BorderStyle = BorderStyle.FixedSingle, Text = "형식: -" };

            lblJob = new Label() { Left = 200, Top = 50, Width = 40, Height = 24, TextAlign = ContentAlignment.MiddleRight, Text = "Job:" };
            cboJob = new ComboBox() { Left = 244, Top = 51, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            cboJob.SelectedIndexChanged += CboJob_SelectedIndexChanged;

            lblSaveOption = new Label() { Left = 480, Top = 50, Width = 70, Height = 24, TextAlign = ContentAlignment.MiddleRight, Text = "저장 옵션:" };
            cboSaveOption = new ComboBox() { Left = 554, Top = 51, Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            cboSaveOption.Items.Add("전체 (이미지·결과 포함) - All");
            cboSaveOption.Items.Add("최소 (이미지·결과 제외) - Minimum");
            cboSaveOption.SelectedIndex = 0;   // 기본: All (기존 동작과 동일)
            _toolTip.SetToolTip(cboSaveOption, "Minimum 은 파일이 작아지지만, 저장 후 다시 열어 결과가 같은지 확인하세요.");

            topPanel.Controls.AddRange(new Control[] { btnLoad, txtFileName, btnSaveAs, btnSave, lblKind, lblJob, cboJob, lblSaveOption, cboSaveOption });
            topPanel.Resize += (s, e) => LayoutTopPanel();

            panelEditorContainer = new Panel() { Dock = DockStyle.Fill };

            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel() { Text = "파일을 불러오세요.", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusStrip.Items.Add(lblStatus);

            // Dock 순서: Fill 을 먼저 추가해야 Top/Bottom 에 가려지지 않음
            this.Controls.Add(panelEditorContainer);
            this.Controls.Add(topPanel);
            this.Controls.Add(statusStrip);

            this.FormClosing += MainForm_FormClosing;

            LayoutTopPanel();
            ShowEmptyEditor();   // 261105@Claude [초기 화면] 빈 편집 화면 + 빈 디스플레이
        }

        /// <summary>
        /// 상단 1줄 배치: [로드][파일이름 ...][다른 이름으로 저장][저장]
        /// </summary>
        private void LayoutTopPanel()
        {
            const int margin = 12;
            const int gap = 8;

            btnSave.Left = topPanel.ClientSize.Width - margin - btnSave.Width;
            btnSaveAs.Left = btnSave.Left - gap - btnSaveAs.Width;

            txtFileName.Left = btnLoad.Right + gap;
            txtFileName.Width = Math.Max(50, btnSaveAs.Left - gap - txtFileName.Left);
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text;
        }

        /// <summary>
        /// 시간이 걸리는 작업 중 대기 커서 + 상단 조작 막기
        /// </summary>
        private void SetBusy(bool busy, string message = null)
        {
            this.UseWaitCursor = busy;
            Cursor.Current = busy ? Cursors.WaitCursor : Cursors.Default;
            topPanel.Enabled = !busy;
            if (message != null) SetStatus(message);
            Application.DoEvents();   // 상태 표시 갱신
        }

        /// <summary>
        /// 문서 상태에 맞게 상단 표시/버튼 갱신
        /// </summary>
        private void UpdateUi()
        {
            bool loaded = _doc != null;
            btnSave.Enabled = loaded;
            btnSaveAs.Enabled = loaded;

            if (loaded)
            {
                txtFileName.Text = Path.GetFileName(_doc.FilePath);
                _toolTip.SetToolTip(txtFileName, _doc.FilePath);
                lblKind.Text = "형식: " + VppInspector.DisplayName(_doc.Kind);
            }
            else
            {
                txtFileName.Text = string.Empty;
                _toolTip.SetToolTip(txtFileName, string.Empty);
                lblKind.Text = "형식: -";
                _fillingJobs = true;
                cboJob.Items.Clear();
                _fillingJobs = false;
                cboJob.Enabled = false;
            }
        }

        #endregion

        #region 불러오기

        private void BtnLoad_Click(object sender, EventArgs e)
        {
            // 열려 있는 파일 저장 확인 (항상 확인)
            if (!ConfirmCloseCurrent()) return;

            string path;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "VisionPro Files (*.vpp)|*.vpp|All Files (*.*)|*.*";
                dlg.Title = "편집할 vpp 파일 선택";
                if (_doc != null) dlg.InitialDirectory = Path.GetDirectoryName(_doc.FilePath);
                if (dlg.ShowDialog() != DialogResult.OK) return;
                path = dlg.FileName;
            }

            // 1) 바이트로만 형식/위험 요소 판정 (카메라/I/O 연결 없음)
            VppFileInfo info;
            try
            {
                SetBusy(true, "파일 형식 확인 중...");
                info = VppInspector.Inspect(path);
            }
            catch (Exception ex)
            {
                SetBusy(false, "파일 형식 확인 실패");
                MessageBox.Show($"파일을 읽을 수 없습니다:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            if (info.Kind == VppKind.Unsupported)
            {
                SetStatus("지원하지 않는 형식: " + info.RootTypeName);
                MessageBox.Show($"지원하지 않는 형식입니다:\n{info.RootTypeName}\n\nQuickBuild / Job / ToolGroup / ToolBlock 파일만 편집할 수 있습니다.", "미지원 형식", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2) 카메라 / I/O 경고 (장비 PC 사용 전제)
            if (!ConfirmHardwareWarning(path, info)) { SetStatus("불러오기 취소"); return; }

            // 3) 이전 파일 정리 → 초기 화면(빈 편집 화면/디스플레이)으로 지운 뒤 불러오기
            //    261105@Claude [로드 시 초기화] 파일 선택·경고 확인 후에 지움 (취소하면 현재 파일 유지)
            CloseCurrent();
            this.Refresh();   // 불러오는 동안(7~13초) 빈 화면이 보이도록 먼저 다시 그림

            try
            {
                SetBusy(true, $"불러오는 중... {Path.GetFileName(path)}");
                if (info.HasAcqFifo || info.HasGigE || info.Kind == VppKind.Unknown) _cameraFileLoaded = true;
                _doc = VppDocument.Load(path);
            }
            catch (Exception ex)
            {
                _doc = null;
                SetBusy(false, "불러오기 실패");
                UpdateUi();
                MessageBox.Show($"파일 불러오기 중 오류 발생:\n{FullMessage(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            UpdateUi();
            FillJobs();

            // 상태 표시
            StringBuilder sb = new StringBuilder();
            sb.Append($"형식: {VppInspector.DisplayName(_doc.Kind)}");
            if (_doc.Kind == VppKind.QuickBuild || _doc.Kind == VppKind.Job) sb.Append($" / Job {_doc.JobCount}개");
            if (_doc.IOEnabled.HasValue) sb.Append(_doc.IOEnabled.Value ? " / I/O 사용: 켜짐(주의)" : " / I/O 사용: 꺼짐");
            if (info.Kind != VppKind.Unknown && info.Kind != _doc.Kind) sb.Append($" / ※ 파일 판정({VppInspector.DisplayName(info.Kind)})과 실제 형식이 다름");
            if (_cameraFileLoaded) sb.Append(" / 카메라 사용 중: 편집기를 종료해야 해제됩니다.");

            // 261105@Claude [보완] 스크립트 컴파일 오류 확인 (불러오기 예외로는 나오지 않음)
            List<string> scriptErrors = GetScriptErrorsSafe();
            if (scriptErrors.Count > 0)
            {
                sb.Insert(0, $"※ 스크립트 오류 {scriptErrors.Count}건 / ");
                const int maxShow = 10;
                string list = string.Join("\n - ", scriptErrors.GetRange(0, Math.Min(maxShow, scriptErrors.Count)));
                string more = scriptErrors.Count > maxShow ? $"\n ... 외 {scriptErrors.Count - maxShow}건" : string.Empty;
                MessageBox.Show(
                    $"스크립트 컴파일 오류가 있습니다 ({scriptErrors.Count}건):\n - {list}{more}\n\n" +
                    "편집은 가능하지만, 이 상태로 저장하면 오류가 있는 스크립트가 그대로 저장됩니다.\n" +
                    "편집 화면의 \"스크립트 작성/편집\"에서 오류 내용을 확인하세요.",
                    "스크립트 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            SetStatus(sb.ToString());
        }

        private List<string> GetScriptErrorsSafe()
        {
            try { return _doc.GetScriptErrors(); }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// 카메라 / I/O 설정이 있는 파일이면 불러오기 전에 경고
        /// </summary>
        private bool ConfirmHardwareWarning(string path, VppFileInfo info)
        {
            List<string> items = new List<string>();
            if (info.HasGigE) items.Add("GigE 카메라 설정");
            if (info.HasAcqFifo) items.Add("영상 취득(AcqFifo) 설정");
            if (info.HasIOSettings) items.Add("I/O·통신(Discrete, TCP/IP) 설정");
            if (items.Count == 0 && info.Kind != VppKind.Unknown) return true;

            string kindText = info.Kind == VppKind.Unknown ? "형식을 미리 판정할 수 없는 파일입니다." : $"형식: {VppInspector.DisplayName(info.Kind)}";
            string itemText = items.Count > 0 ? "포함된 설정:\n - " + string.Join("\n - ", items) : "카메라/I/O 설정 포함 여부를 확인할 수 없습니다.";

            string msg =
                $"{Path.GetFileName(path)}\n{kindText}\n\n{itemText}\n\n" +
                "불러오면 카메라 또는 I/O·통신에 연결을 시도할 수 있습니다.\n" +
                "장비 PC에서는 QuickBuild / 생산 프로그램을 먼저 종료하세요.\n" +
                "(같은 카메라를 두 프로그램이 동시에 쓰면 연결이 끊어질 수 있습니다.)\n" +
                "※ 카메라는 파일을 닫아도 해제되지 않고, 이 편집기를 종료해야 해제됩니다.\n\n" +
                "계속 불러올까요?";
            return MessageBox.Show(msg, "카메라 / I/O 주의", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>
        /// Job 콤보 채우기 (QuickBuild/Job 만 사용) 후 첫 번째 대상 편집 화면 연결
        /// </summary>
        private void FillJobs()
        {
            _fillingJobs = true;
            cboJob.Items.Clear();
            for (int i = 0; i < _doc.JobCount; i++)
            {
                cboJob.Items.Add($"{i}: {_doc.GetJobName(i)}");
            }
            cboJob.Enabled = _doc.JobCount > 1;     // Job 이 여러 개일 때만 선택 가능
            if (cboJob.Items.Count > 0) cboJob.SelectedIndex = 0;
            _fillingJobs = false;

            BindEditor(_doc.GetEditSubject(0));
        }

        private void CboJob_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_fillingJobs || _doc == null || cboJob.SelectedIndex < 0) return;
            BindEditor(_doc.GetEditSubject(cboJob.SelectedIndex));
        }

        #endregion

        #region 편집 화면

        /// <summary>
        /// 대상에 맞는 편집 화면을 새로 만들어 연결
        /// - CogToolBlock → CogToolBlockEditV2 (반드시 CogToolBlock 형으로 Subject 연결)
        /// - CogToolGroup → CogToolGroupEditV2
        /// </summary>
        private void BindEditor(CogToolGroup subject)
        {
            DetachEditor();

            if (subject == null)
            {
                SetStatus("편집할 ToolGroup/ToolBlock 을 찾을 수 없습니다.");
                return;
            }

            // 261105@Claude [보완] 편집 화면 생성/연결 오류 시 프로그램이 종료되지 않도록 처리
            try
            {
                // 261105@Claude [실행 원복] SuspendElectricRuns(자동 실행 막기) 제거 → VisionPro 원래 동작
                if (subject is CogToolBlock toolBlock)
                {
                    CogToolBlockEditV2 ed = new CogToolBlockEditV2() { Dock = DockStyle.Fill };
                    _editor = ed;
                    panelEditorContainer.Controls.Add(ed);
                    ed.Subject = toolBlock;          // CogToolBlock 형 Subject 사용 (Inputs/Outputs, 스크립트)
                }
                else
                {
                    CogToolGroupEditV2 ed = new CogToolGroupEditV2() { Dock = DockStyle.Fill };
                    _editor = ed;
                    panelEditorContainer.Controls.Add(ed);
                    ed.Subject = subject;
                }

                HideFileButtons(_editor);
            }
            catch (Exception ex)
            {
                ShowEmptyEditor();
                SetStatus("편집 화면 표시 실패");
                MessageBox.Show($"편집 화면을 표시할 수 없습니다:\n{FullMessage(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 261105@Claude [초기 화면] 파일이 없을 때의 빈 편집 화면 (왼쪽 빈 도구 트리 + 오른쪽 빈 디스플레이)
        /// - Subject 가 없는 CogToolGroupEditV2. 파일 불러오기 화면과 같은 배치.
        /// - 빈 상태에서도 툴바 "열기/저장"이 눌리므로 숨김 (불러오기 흐름을 거치지 않고 파일이 열리는 것 방지)
        /// </summary>
        private void ShowEmptyEditor()
        {
            DetachEditor();
            try
            {
                CogToolGroupEditV2 ed = new CogToolGroupEditV2() { Dock = DockStyle.Fill };
                _editor = ed;
                panelEditorContainer.Controls.Add(ed);
                ed.Subject = null;
                HideFileButtons(ed);
            }
            catch
            {
                DetachEditor();   // 빈 화면 표시 실패는 무시 (편집 기능과 무관)
            }
        }

        /// <summary>
        /// 편집 화면 연결 끊고 해제
        /// </summary>
        private void DetachEditor()
        {
            if (_editor == null) return;
            try
            {
                if (_editor is CogToolBlockEditV2 tbEd) tbEd.Subject = null;
                else if (_editor is CogToolGroupEditV2 tgEd) tgEd.Subject = null;
                panelEditorContainer.Controls.Remove(_editor);
                _editor.Dispose();
            }
            catch { }
            _editor = null;
        }

        /// <summary>
        /// 편집 화면 툴바에서 숨길 버튼 (툴팁 문구 기준, 한글/영문)
        /// - 열기 / 저장 / 다른 이름으로 저장 : 편집 화면 자체 파일 기능 → 상단 저장(백업 포함)과 혼동 방지
        ///   (QuickBuild/Job 에서 이 버튼으로 저장하면 ToolGroup 만 따로 저장되어 파일 형식이 바뀜)
        /// 261105@Claude [실행 원복] 사용자 요청으로 "실행", "루프"는 숨기지 않음 (테스트에 필요)
        ///   (이전: "실행", "루프", "연속 실행", "Run", "Loop", "Continuous" 도 숨겼음)
        /// </summary>
        private static readonly string[] HiddenButtonTips =
        {
            "열기", "저장", "다른 이름으로 저장",
            "Open", "Save", "Save As"
        };

        /// <summary>
        /// 편집 화면 툴바에서 파일 버튼 숨김
        /// ※ 버튼 툴팁 문구로 찾는 방식 (VisionPro 언어 설정에 따라 다를 수 있음)
        /// </summary>
        private void HideFileButtons(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ToolBar bar)
                {
                    foreach (ToolBarButton b in bar.Buttons)
                    {
                        string tip = (b.ToolTipText ?? string.Empty).Trim();
                        foreach (string hidden in HiddenButtonTips)
                        {
                            // 정확히 일치 또는 "Run (F5)" 처럼 뒤에 단축키 등이 붙은 경우
                            if (tip.Equals(hidden, StringComparison.OrdinalIgnoreCase) ||
                                tip.StartsWith(hidden + " ", StringComparison.OrdinalIgnoreCase) ||
                                tip.StartsWith(hidden + "(", StringComparison.OrdinalIgnoreCase))
                            {
                                b.Visible = false;
                                break;
                            }
                        }
                    }
                }
                HideFileButtons(c);
            }
        }

        #endregion

        #region 저장

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_doc == null) return;
            if (SaveCurrent(_doc.FilePath, out string backup))
            {
                string backupText = backup != null ? $"\n백업: {Path.GetFileName(backup)}" : string.Empty;
                MessageBox.Show($"저장되었습니다:\n{Path.GetFileName(_doc.FilePath)}{backupText}", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnSaveAs_Click(object sender, EventArgs e)
        {
            if (_doc == null) return;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "VisionPro Files (*.vpp)|*.vpp|All Files (*.*)|*.*";
                dlg.Title = "다른 이름으로 저장";
                dlg.DefaultExt = "vpp";
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                dlg.InitialDirectory = Path.GetDirectoryName(_doc.FilePath);
                dlg.FileName = Path.GetFileName(_doc.FilePath);

                if (dlg.ShowDialog() != DialogResult.OK) return;

                if (SaveCurrent(dlg.FileName, out string backup))
                {
                    UpdateUi();   // 현재 파일이 새 파일로 바뀜
                    string backupText = backup != null ? $"\n백업: {Path.GetFileName(backup)}" : string.Empty;
                    MessageBox.Show($"저장되었습니다:\n{Path.GetFileName(_doc.FilePath)}{backupText}", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        /// <summary>
        /// 선택한 저장 옵션으로 저장. 성공 시 true.
        /// </summary>
        private bool SaveCurrent(string path, out string backupPath)
        {
            backupPath = null;
            CogSerializationOptionsConstants options = cboSaveOption.SelectedIndex == 1
                ? CogSerializationOptionsConstants.Minimum
                : CogSerializationOptionsConstants.All;

            // 261105@Claude [보완] Minimum 저장은 입력 이미지·결과가 영구히 빠지므로 확인
            if (options == CogSerializationOptionsConstants.Minimum)
            {
                DialogResult r = MessageBox.Show(
                    "저장 옵션이 \"최소(Minimum)\"입니다.\n\n" +
                    "툴의 입력 이미지와 마지막 실행 결과가 빠진 채로 저장됩니다.\n" +
                    "(학습 패턴·학습 이미지는 유지됩니다.)\n\n계속 저장할까요?",
                    "저장 옵션 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return false;
            }

            try
            {
                SetBusy(true, $"저장 중... {Path.GetFileName(path)}");
                backupPath = _doc.Save(path, options);
                SetStatus($"저장 완료: {Path.GetFileName(_doc.FilePath)} ({(options == CogSerializationOptionsConstants.All ? "All" : "Minimum")})" +
                          (backupPath != null ? $" / 백업: {Path.GetFileName(backupPath)}" : string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("저장 실패 (원본 파일은 변경되지 않았습니다)");
                MessageBox.Show($"저장 중 오류 발생:\n{FullMessage(ex)}\n\n원본 파일은 변경되지 않았습니다.", "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        #endregion

        #region 닫기 / 정리

        /// <summary>
        /// 열려 있는 파일이 있으면 저장 여부 확인 (항상 확인). 계속 진행해도 되면 true.
        /// </summary>
        private bool ConfirmCloseCurrent()
        {
            if (_doc == null) return true;

            DialogResult r = MessageBox.Show(
                $"현재 파일을 저장할까요?\n{Path.GetFileName(_doc.FilePath)}",
                "저장 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveCurrent(_doc.FilePath, out _);
            return true;
        }

        /// <summary>
        /// 정리 순서: 편집 화면 Subject 끊기 → 정지 → Shutdown/Dispose → 편집 화면 해제
        /// 261105@Claude [로드 시 초기화] 정리 후 빈 편집 화면 표시 (프로그램 종료 시에는 showEmpty=false)
        /// </summary>
        private void CloseCurrent(bool showEmpty = true)
        {
            if (_editor is CogToolBlockEditV2 tbEd) tbEd.Subject = null;
            else if (_editor is CogToolGroupEditV2 tgEd) tgEd.Subject = null;

            if (_doc != null)
            {
                _doc.Dispose();   // StopRunning → Shutdown / Dispose
                _doc = null;
            }

            DetachEditor();
            if (showEmpty) ShowEmptyEditor();
            UpdateUi();
        }

        /// <summary>
        /// 261105@Claude 카메라(프레임 그래버) 연결 해제 - 프로그램 종료 시에만 호출
        /// - 이 버전에는 CogFrameGrabbers.Disconnect 정적 함수가 없어 하나씩 해제
        /// - CogFrameGrabbers 생성 자체가 카메라를 검색하므로, 카메라 파일을 불러온 경우에만 실행
        /// - [카메라 시험 결과] 파일을 닫고 Shutdown + Disconnect(false/true) + GC + 20초 대기를 해도
        ///   다른 프로세스에서 "요청한 하드웨어를 다른 프로세스에서 이미 사용하고 있습니다" 발생.
        ///   → GigE 카메라는 이 프로그램이 종료되어야 해제됨. 그래서 파일 닫을 때는 호출하지 않음.
        /// </summary>
        private void ReleaseCameras()
        {
            if (!_cameraFileLoaded) return;
            try
            {
                CogFrameGrabbers frameGrabbers = new CogFrameGrabbers();
                foreach (ICogFrameGrabber fg in frameGrabbers)
                {
                    fg.Disconnect(false);
                }
            }
            catch { }
            _cameraFileLoaded = false;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmCloseCurrent())
            {
                e.Cancel = true;
                return;
            }

            try
            {
                CloseCurrent(false);   // 종료 중이므로 빈 화면은 만들지 않음
                ReleaseCameras();   // 카메라(프레임 그래버) 연결 해제
            }
            catch
            {
                // 종료 중 정리 오류는 무시
            }
        }

        /// <summary>
        /// 내부 예외까지 포함한 메시지 (스크립트 컴파일 오류 등 확인용)
        /// </summary>
        private static string FullMessage(Exception ex)
        {
            StringBuilder sb = new StringBuilder(ex.Message);
            Exception inner = ex.InnerException;
            while (inner != null)
            {
                sb.Append("\n→ ").Append(inner.Message);
                inner = inner.InnerException;
            }
            return sb.ToString();
        }

        #endregion
    }
}
