using TaleWorlds.Library;
using TaleWorlds.Localization;

public class TextViewModel : ViewModel
{
	private TextObject _textObject;

	private string _text;

	public TextObject TextObject
	{
		get
		{
			return _textObject;
		}
		set
		{
			_textObject = value;
		}
	}

	[DataSourceProperty]
	public string Text
	{
		get
		{
			return _text;
		}
		set
		{
			if (!(_text == value))
			{
				_text = value;
				OnPropertyChanged("Text");
			}
		}
	}

	public TextViewModel(TextObject text)
	{
		TextObject = text;
	}

	public override void RefreshValues()
	{
		base.RefreshValues();
		TextObject = TextObject;
	}
}
