using System;
using System.Linq;

namespace InventorXrSo.Core.Ui
{
    /// <summary>Literal text proposal. Confirmation changes the draft only; CAD validates expressions at Preview.</summary>
    public sealed class TextEntry
    {
        public TextEntry(string id,string value,int maxLength)
        {
            if(string.IsNullOrWhiteSpace(id) || maxLength<1 || maxLength>16384) throw new ArgumentException("Invalid text field.");
            Id=id;MaxLength=maxLength;Value=value ?? "";
            if(Value.Length>maxLength || Value.Any(char.IsControl)) throw new ArgumentException("Invalid initial text.");
        }
        public string Id {get;}
        public int MaxLength {get;}
        public string Value {get;private set;}
        public string Buffer {get;private set;}
        public int Cursor {get;private set;}
        public bool Editing=>Buffer!=null;
        public string Error {get;private set;}="";
        public event Action Changed,Committed;
        public string Display
        {
            get
            {
                if(!Editing) return Value;
                int start=Math.Max(0,Cursor-30),end=Math.Min(Buffer.Length,start+60);
                return (start>0 ? "…" : "")+Buffer.Substring(start,Cursor-start)+"│"+Buffer.Substring(Cursor,end-Cursor)+(end<Buffer.Length ? "…" : "");
            }
        }
        public void BeginEdit() {Buffer=Value;Cursor=Buffer.Length;Error="";Changed?.Invoke();}
        public bool Replace(string text)
        {
            if(!Editing || text==null || text.Length>MaxLength || text.Any(char.IsControl)) return false;
            Buffer=text;Cursor=text.Length;Error="";Changed?.Invoke();return true;
        }
        public bool Type(char value)
        {
            if(!Editing || char.IsControl(value)) return false;
            if(Buffer.Length>=MaxLength) {Error="Massimo "+MaxLength+" caratteri.";Changed?.Invoke();return false;}
            Buffer=Buffer.Insert(Cursor++,value.ToString());Error="";Changed?.Invoke();return true;
        }
        public void MoveCursor(int direction)
        {if(!Editing) return;Cursor=Math.Max(0,Math.Min(Buffer.Length,Cursor+Math.Sign(direction)));Changed?.Invoke();}
        public void Backspace()
        {if(!Editing || Cursor==0) return;Buffer=Buffer.Remove(--Cursor,1);Error="";Changed?.Invoke();}
        public bool Commit(out string reason)
        {
            reason="";if(!Editing) return false;
            if(string.IsNullOrWhiteSpace(Buffer)) {reason=Error="Inserisci un testo non vuoto.";Changed?.Invoke();return false;}
            Value=Buffer;Buffer=null;Error="";Changed?.Invoke();Committed?.Invoke();return true;
        }
        public void CancelEdit() {if(!Editing) return;Buffer=null;Error="";Changed?.Invoke();}
    }
}
