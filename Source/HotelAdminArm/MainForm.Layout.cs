using System.Data;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    private TabControl _bookingSections=null!,_staySections=null!,_serviceSections=null!,_paymentSections=null!;
    private readonly Label[] _statValues=new Label[6];

    private static TabControl Sections(params (string Title,Control Content)[] sections)
    {
        var tabs=new TabControl{Dock=DockStyle.Fill,Padding=new Point(12,5),Multiline=true};
        foreach(var (title,content) in sections)
        {
            var page=CreateTabPage(title);page.Padding=new Padding(4);content.Dock=DockStyle.Fill;
            page.Controls.Add(content);tabs.TabPages.Add(page);
        }
        return tabs;
    }

    private static Control WithActions(Control content,params Control[] actions)
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Dock=DockStyle.Fill;layout.Controls.Add(content,0,0);layout.Controls.Add(ButtonsPanel(actions),0,1);return layout;
    }

    private static Control PinEditor(TableLayoutPanel fields)
    {
        var last=fields.RowCount-1;
        var actions=fields.Controls.Cast<Control>().OfType<WrappingButtonPanel>().FirstOrDefault(c=>fields.GetRow(c)==last);
        if(actions is null)throw new InvalidOperationException("У формы нет панели действий.");
        fields.Controls.Remove(actions);
        foreach(var blank in fields.Controls.Cast<Control>().Where(c=>fields.GetRow(c)==last).ToArray()){fields.Controls.Remove(blank);blank.Dispose();}
        fields.RowCount=last;if(fields.RowStyles.Count>last)fields.RowStyles.RemoveAt(last);
        fields.Tag=null;fields.AutoScroll=false;fields.AutoSize=true;fields.AutoSizeMode=AutoSizeMode.GrowAndShrink;fields.Dock=DockStyle.Top;
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=HotelBranding.Surface};scroll.Controls.Add(fields);
        void LimitWidth(){var scale=Math.Max(1,fields.DeviceDpi/96f*fields.Font.Size/10f);fields.MaximumSize=new Size(Math.Max(100,Math.Min(scroll.ClientSize.Width,(int)(460*scale))),0);}
        scroll.SizeChanged+=(_,_)=>LimitWidth();fields.FontChanged+=(_,_)=>LimitWidth();
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=HotelBranding.Surface};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(scroll,0,0);layout.Controls.Add(actions,0,1);return layout;
    }

    private static Control Workspace(Control list,TableLayoutPanel editor)=>new ResponsiveWorkspace(list,PinEditor(editor));

    private static Control FilterBox(string title,Control input,int width=180)
    {
        var box=new TableLayoutPanel{Width=width,Height=64,MinimumSize=new Size(width,64),ColumnCount=1,RowCount=2,Margin=new Padding(4)};
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));box.RowStyles.Add(new RowStyle(SizeType.AutoSize));box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        box.Controls.Add(new Label{Text=title,AutoSize=true,Dock=DockStyle.Fill},0,0);
        input.Anchor=AnchorStyles.Left|AnchorStyles.Right;box.Controls.Add(input,0,1);return box;
    }

    private static Control EmptyAwareGrid(DataGridView grid,string message)
    {
        var host=new Panel{Dock=DockStyle.Fill,BackColor=HotelBranding.Background};
        var empty=new Label{Dock=DockStyle.Fill,Text=message,TextAlign=ContentAlignment.TopLeft,Padding=new Padding(16),ForeColor=HotelBranding.PrimaryDark};
        host.Controls.Add(grid);host.Controls.Add(empty);
        void Update(){var count=grid.DataSource is DataTable table?table.DefaultView.Count:0;grid.Visible=count>0;empty.Visible=count==0;}
        grid.DataSourceChanged+=(_,_)=>Update();Update();return host;
    }

    private static void FitGridColumns(DataGridView grid)
    {
        if(grid.Tag as string=="Calendar" || grid.Columns.Count==0)return;
        var scale=Math.Max(1,grid.DeviceDpi/96f*grid.Font.Size/10f);
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        foreach(DataGridViewColumn column in grid.Columns)
        {
            if(!column.Visible)continue;
            var name=column.Name;
            var flexible=new[]{"Гость","Плательщик","Услуга","Получатель питания","Примечание","Комментарий","Описание","Проблема","Адрес","Email","Фамилия","Имя","Отчество","Брони"}.Contains(name);
            column.MinimumWidth=(int)((flexible?110:70)*scale);
            column.AutoSizeMode=flexible?DataGridViewAutoSizeColumnMode.Fill:DataGridViewAutoSizeColumnMode.None;
            if(flexible)column.FillWeight=name is "Описание" or "Примечание" or "Комментарий" or "Проблема"?180:100;
            else
            {
                var date=name.Contains("Дата",StringComparison.OrdinalIgnoreCase) || name is "Заезд" or "Выезд" or "Плановый выезд";
                if(date)column.MinimumWidth=(int)((name=="Дата"?175:110)*scale);
                column.Width=Math.Max(column.MinimumWidth,Math.Min((int)(220*scale),column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells,true)));
            }
        }
        if(!grid.Columns.Cast<DataGridViewColumn>().Any(c=>c.Visible && c.AutoSizeMode==DataGridViewAutoSizeColumnMode.Fill))
        {
            var last=grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c=>c.Name=="Тип")??grid.Columns.Cast<DataGridViewColumn>().LastOrDefault(c=>c.Visible);
            if(last is not null)last.AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
        }
    }
}
