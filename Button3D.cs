using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VProEditAll
{
    /// <summary>
    /// 261105@Claude [버튼 입체감] 그라데이션 + 그림자로 그리는 버튼 (권장안 B)
    /// - 기본   : 위(밝음) → 아래(어두움) 그라데이션 + 오른쪽 아래 그림자
    /// - 마우스 : 옅은 파랑 그라데이션
    /// - 눌림   : 파랑 그라데이션, 그림자 없이 1px 들어감
    /// - 비활성 : 옅은 회색 + 회색 글자
    /// Windows 11 테마에서는 기본 버튼 스타일(Standard/System/Popup)이 모두 평평하게 보여 직접 그림.
    /// </summary>
    public class Button3D : Button
    {
        private bool _hover;
        private bool _pressed;

        public Button3D()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(System.EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(System.EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false; Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);

            const int shadow = 2;
            Rectangle r = new Rectangle(0, 0, Width - 1 - shadow, Height - 1 - shadow);
            bool pressed = _pressed && Enabled;

            Color top, bottom, border;
            if (!Enabled)
            {
                // 261105@Claude [비활성 입체감] 비활성도 그라데이션을 뚜렷하게 (이전: 246→228, 테두리 185 로 거의 평평)
                top = Color.FromArgb(250, 250, 250); bottom = Color.FromArgb(212, 212, 212); border = Color.FromArgb(150, 150, 150);
            }
            else if (pressed)
            {
                top = Color.FromArgb(190, 210, 235); bottom = Color.FromArgb(225, 235, 248); border = Color.FromArgb(90, 120, 160);
            }
            else if (_hover)
            {
                top = Color.FromArgb(255, 255, 255); bottom = Color.FromArgb(205, 225, 245); border = Color.FromArgb(90, 120, 160);
            }
            else
            {
                top = Color.FromArgb(252, 252, 252); bottom = Color.FromArgb(215, 215, 215); border = Color.FromArgb(120, 120, 120);
            }

            if (pressed)
            {
                r.Offset(1, 1);   // 들어간 효과
            }
            else
            {
                // 261105@Claude [비활성 입체감] 비활성도 그림자 표시 (조금 옅게). 이전: 활성일 때만 그림자
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(Enabled ? 70 : 45, 0, 0, 0)))
                    g.FillRectangle(sh, r.X + shadow, r.Y + shadow, r.Width, r.Height);
            }

            using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, r.Height + 1), top, bottom, 90f))
                g.FillRectangle(br, r);
            using (Pen p = new Pen(border))
                g.DrawRectangle(p, r);

            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Color.Black : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -3, -3));
            }
        }
    }
}
