using HotelAdminArm.Data;

namespace HotelAdminArm;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var index=Array.IndexOf(args,"--database");
            if(index>=0){if(index+1>=args.Length)throw new ArgumentException("После --database укажите файл базы.");Database.DatabasePath=Path.GetFullPath(args[index+1]);}
            var screenshots=Array.IndexOf(args,"--screenshots");
            if(screenshots>=0 && (index<0 || screenshots+1>=args.Length))throw new ArgumentException("Для снимков укажите отдельную базу: --database файл --screenshots каталог");
            try { Database.Initialize(); }
            catch(Exception error)
            {
                if(screenshots>=0)throw;
                if(MessageBox.Show("Не удалось открыть базу:\n"+error.Message+"\n\nВосстановить резервную копию?",HotelBranding.SystemTitle,MessageBoxButtons.YesNo,MessageBoxIcon.Error)!=DialogResult.Yes)return 1;
                using var dialog=new OpenFileDialog{Filter="База гостиницы|*.db"};if(dialog.ShowDialog()!=DialogResult.OK)return 1;
                Database.RestoreFrom(dialog.FileName);Database.Initialize(false);
            }
            using var form=new MainForm();
            if(screenshots>=0)
            {
                form.ErrorHandler=e=>throw new InvalidOperationException("Ошибка интерфейса",e);
                var folder=Path.GetFullPath(args[screenshots+1]);Directory.CreateDirectory(folder);
                form.WindowState=FormWindowState.Normal;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-10000,-10000);form.Size=new Size(1600,1000);
                form.Show();form.Size=new Size(1600,1000);Application.DoEvents();
                var tabs=FindTabs(form);
                for(var i=0;i<tabs.TabCount;i++)
                {
                    tabs.SelectedIndex=i;Application.DoEvents();
                    if(i==1){form.SelectRoomForScreenshot();Application.DoEvents();}
                    using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
                    bitmap.Save(Path.Combine(folder,$"{i+1:00}.png"),System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Close();return 0;
            }
            Application.Run(form);return 0;
        }
        catch(Exception e)
        {
            if(args.Contains("--screenshots")){Console.Error.WriteLine(e);return 1;}
            MessageBox.Show(e.Message,HotelBranding.SystemTitle,MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
        }
    }
    private static TabControl FindTabs(Control control)
    {
        foreach(Control child in control.Controls){if(child is TabControl tabs)return tabs;try{return FindTabs(child);}catch(InvalidOperationException){}}
        throw new InvalidOperationException("Вкладки не найдены.");
    }
}
