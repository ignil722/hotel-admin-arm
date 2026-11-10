namespace HotelAdminArm;

// FlowLayoutPanel otherwise reports one unbounded row to TableLayoutPanel,
// forcing percentage columns wider than the window before wrapping can occur.
internal sealed class WrappingButtonPanel : FlowLayoutPanel
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width=Parent?.ClientSize.Width??proposedSize.Width;
        if(Parent is TableLayoutPanel table && table.ColumnStyles.Count>0)
        {
            var position=table.GetPositionFromControl(this);
            var span=table.GetColumnSpan(this);
            var total=table.ClientSize.Width-table.Padding.Horizontal;
            var absolute=table.ColumnStyles.Cast<ColumnStyle>().Where(s=>s.SizeType==SizeType.Absolute).Sum(s=>s.Width);
            var percent=table.ColumnStyles.Cast<ColumnStyle>().Where(s=>s.SizeType==SizeType.Percent).Sum(s=>s.Width);
            width=0;
            for(var i=Math.Max(0,position.Column);i<Math.Min(table.ColumnStyles.Count,position.Column+span);i++)
            {
                var style=table.ColumnStyles[i];
                width+=(int)(style.SizeType==SizeType.Absolute?style.Width:style.SizeType==SizeType.Percent?(total-absolute)*style.Width/Math.Max(1,percent):0);
            }
        }
        width=Math.Max(100,width-Margin.Horizontal-Padding.Horizontal);
        if(proposedSize.Width>0)width=Math.Min(width,proposedSize.Width);
        foreach(Control child in Controls)
            if(child is Button or Label or CheckBox)
                child.MaximumSize=new Size(Math.Max(child.MinimumSize.Width,Math.Max(80,width-child.Margin.Horizontal)),0);
        var preferred=base.GetPreferredSize(new Size(width,0));
        return new Size(Math.Min(width,preferred.Width),preferred.Height);
    }
}
