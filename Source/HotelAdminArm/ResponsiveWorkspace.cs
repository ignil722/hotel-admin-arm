namespace HotelAdminArm;

internal sealed class ResponsiveWorkspace : SplitContainer
{
    private bool _arranging;
    private bool? _wide;
    private double _wideRatio=0.7, _narrowRatio=0.55;

    public ResponsiveWorkspace(Control list,Control editor)
    {
        Dock=DockStyle.Fill;BorderStyle=BorderStyle.None;SplitterWidth=6;
        Panel1.Controls.Add(list);Panel2.Controls.Add(editor);
        list.Dock=DockStyle.Fill;editor.Dock=DockStyle.Fill;
        SplitterMoved+=(_,_)=>{
            if(_arranging || _wide is null)return;
            var length=Orientation==Orientation.Vertical?ClientSize.Width:ClientSize.Height;
            if(length<=0)return;
            if(_wide.Value)_wideRatio=SplitterDistance/(double)length;else _narrowRatio=SplitterDistance/(double)length;
        };
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if(!_arranging && ClientSize.Width>0 && ClientSize.Height>0)
        {
            _arranging=true;
            try
            {
                var scale=Math.Max(1,DeviceDpi/96f*Font.Size/10f);
                var wide=ClientSize.Width/scale>=1100;
                var changed=_wide!=wide;
                Panel1MinSize=0;Panel2MinSize=0;
                Orientation=wide?Orientation.Vertical:Orientation.Horizontal;
                var length=wide?ClientSize.Width:ClientSize.Height;
                if(length>SplitterWidth+4)
                {
                    int distance;
                    if(wide)
                    {
                        var editorWidth=changed?(int)(420*scale):(int)(length*(1-_wideRatio));
                        editorWidth=Math.Clamp(editorWidth,(int)(360*scale),(int)(460*scale));
                        distance=length-editorWidth-SplitterWidth;
                    }
                    else distance=(int)(length*_narrowRatio);
                    var minimum=Math.Max(1,Math.Min((int)(120*scale),(length-SplitterWidth)/3));
                    SplitterDistance=Math.Clamp(distance,minimum,length-SplitterWidth-minimum);
                    Panel1MinSize=minimum;Panel2MinSize=minimum;
                }
                _wide=wide;
            }
            finally{_arranging=false;}
        }
        base.OnLayout(e);
    }
}
