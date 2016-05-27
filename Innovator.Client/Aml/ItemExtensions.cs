﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Xml.Linq;

namespace Innovator.Client
{
  /// <summary>
  /// Various extension methods pertaining to various AML interfaces
  /// </summary>
  public static class ItemExtensions
  {
    /// <summary>
    /// Apply this item in the database
    /// </summary>
    public static IReadOnlyResult Apply(this IReadOnlyItem item, IConnection conn)
    {
      var aml = conn.AmlContext;
      var query = item.ToString();
      return aml.FromXml(conn.Process(query), query, conn);
    }
    /// <summary>Download the file represented by the property </summary>
    /// <returns>This will return the file contents for item properties of type 'File' and 
    /// image properties that point to vault files</returns>
    public static Stream AsFile(this IReadOnlyProperty prop, IConnection conn)
    {
      return prop.AsFile(conn, false).Value;
    }
    /// <summary>Asynchronously download the file represented by the property</summary>
    /// <returns>This will return the file contents for item properties of type 'File' and 
    /// image properties that point to vault files</returns>
    public static IPromise<Stream> AsFile(this IReadOnlyProperty prop, IConnection conn, bool async)
    {
      if (prop == null)
        throw new ArgumentNullException("prop");

      var file = prop.AsItem();
      if (file.Exists && file.Type().Value == "File")
      {
        var cmd = new Command(file.ToString()).WithAction(CommandAction.DownloadFile);
        return conn.ProcessAsync(cmd, async);
      }

      var url = prop.AsString("");
      if (url.StartsWith("vault:///?fileId="))
      {
        var id = url.Substring(17);
        var cmd = new Command("<Item type='File' action='get' id='@0' />", id).WithAction(CommandAction.DownloadFile);
        return conn.ProcessAsync(cmd, async);
      }

      // Random url used for testing the relative path
      var baseUri = new Uri("http://www.test.com/a/b/c/d/e/f");
      Uri testResult;
      if (Uri.TryCreate(baseUri, url, out testResult))
      {
        url = conn.MapClientUrl(url);

        byte[] data;
        if (Factory.ImageCache.TryGetValue(url, out data))
        {
          return Promises.Resolved((Stream)new MemoryStream(data));
        }
        return Factory.DefaultService.Invoke().Execute("GET", url, null, null, async, null).Convert(r => {
          var buffer = new MemoryStream();
          r.AsStream.CopyTo(buffer);
          Factory.ImageCache.TryAdd(url, buffer.ToArray());
          buffer.Position = 0;
          return (Stream)buffer;
        });
      }

      return Promises.Rejected<Stream>(
        new ArgumentException(string.Format("Property '{0}' does not reference a file to download", prop.Name), "prop"));
    }
    /// <summary>Determine if the <c>classification</c> starts with one of the specified root paths</summary>
    public static bool ClassStartsWith(this IReadOnlyItem item, params string[] roots)
    {
      if (roots == null) return false;
      var path = item.Classification().Value;

      foreach (var root in roots)
      {
        if (string.IsNullOrEmpty(root)) return true;
        if (!string.IsNullOrEmpty(path)
          && (path.Equals(root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))) return true;
      }
      return false;
    }
    /// <summary>
    /// Send an AML edit query to the database with the body of the Item tab being the contents specified
    /// </summary>
    /// <param name="contents">Body of the Item action='edit' tag</param>
    public static IReadOnlyResult Edit(this IItemRef item, IConnection conn, params object[] contents)
    {
      var aml = conn.AmlContext;
      var editItem = aml.Item(aml.Action("edit"), aml.Type(item.TypeName()), aml.Id(item.Id()));
      foreach (var content in contents)
        editItem.Add(content);
      var query = editItem.ToString();
      return aml.FromXml(conn.Process(query), query, conn);
    }
    /// <summary>
    /// Retrieve the lock status from the database
    /// </summary>
    /// <remarks>If the item is editable, the <c>locked_by_id</c> property will be updated</remarks>
    public static LockStatusType FetchLockStatus(this IReadOnlyItem item, IConnection conn)
    {
      var aml = conn.AmlContext;
      return aml.Item(aml.Action("get"),
        aml.Type(item.Type().Value),
        aml.Id(item.Id()),
        aml.Select("locked_by_id")
      ).Apply(conn).AssertItem().LockStatus(conn);
    }
    /// <summary>
    /// Returns either the first item from the enumerable or a 'null' item (where <c>Exists</c> is <c>false</c>)
    /// if there are no items
    /// </summary>
    public static IReadOnlyItem FirstOrNullItem(this IEnumerable<IReadOnlyItem> items)
    {
      return items.FirstOrDefault() ?? Item.NullItem;
    }
    /// <summary>
    /// Returns either the first matching item from the enumerable or a 'null' item (where <c>Exists</c> is <c>false</c>)
    /// if there are no items which match the predicate
    /// </summary>
    /// <param name="predicate">Criteria to match</param>
    public static IReadOnlyItem FirstOrNullItem(this IEnumerable<IReadOnlyItem> items, Func<IReadOnlyItem, bool> predicate)
    {
      return items.FirstOrDefault(predicate) ?? Item.NullItem;
    }
    /// <summary>
    /// Indicates that the property is neither null nor empty
    /// </summary>
    /// <remarks>If the property is empty but has <c>is_null='0'</c>, then this will return <c>true</c></remarks>
    public static bool HasValue(this IReadOnlyProperty prop)
    {
      return prop.Exists 
        && (!string.IsNullOrEmpty(prop.Value)
          || prop.IsNull().AsBoolean() == false);
    }
    /// <summary>
    /// Indicates that the attribute is neither null nor empty
    /// </summary>
    public static bool HasValue(this IReadOnlyAttribute attr)
    {
      return attr.Exists && !string.IsNullOrEmpty(attr.Value);
    }
    public static void Lock(this IItemRef item, IConnection conn)
    {
      var result = conn.Lock(item.TypeName(), item.Id());
      var editable = item as IItem;
      if (editable != null)
        editable.LockedById().Set(result.LockedById().Value);
    }
    public static LockStatusType LockStatus(this IReadOnlyItem item, IConnection conn)
    {
      var id = item.LockedById().AsString(null);
      if (id == null) return LockStatusType.NotLocked;
      if (id == conn.UserId) return LockStatusType.LockedByUser;
      return LockStatusType.LockedByOther;
    }
    internal static IItem Mutable(this IReadOnlyItem item)
    {
      var result = item as IItem;
      if (result == null) throw new NotImplementedException();
      return result;
    }
    /// <summary>
    /// Promote the itme to the specified state
    /// </summary>
    /// <param name="item">Item to promote</param>
    /// <param name="conn">Connection to execute the promotion on</param>
    /// <param name="state">New state of the item</param>
    /// <param name="comments">Comments to include with the promotion</param>
    /// <example>
    /// <code lang="C#">
    /// // Promote the item. Throw an exception if an error occurs.
    /// comp.Promote(conn, "Released").AssertNoError();
    /// </code>
    /// </example>
    public static IReadOnlyResult Promote(this IItemRef item, IConnection conn, string state, string comments = null)
    {
      return conn.Promote(item.TypeName(), item.Id(), state, comments);
    }
    /// <summary>
    /// Renders an AML node to XML
    /// </summary>
    public static XElement ToXml(this IAmlNode node)
    {
      var doc = new XDocument();
      using (var writer = doc.CreateWriter())
      {
        node.ToAml(writer);
      }
      return doc.Root;
    }
    public static void Unlock(this IItemRef item, IConnection conn)
    {
      conn.Unlock(item.TypeName(), item.Id());
      var editable = item as IItem;
      if (editable != null)
        editable.LockedById().Remove();
    }
    
    /// <summary>
    /// Maps an item to a new object.  If there are properties which couldn't be found during the
    /// initial mapping, the method will query the database and run the mapper again with the
    /// database results
    /// </summary>
    /// <param name="mapper">Function which creates a new object by referencing values from the item</param>
    public static T LazyMap<T>(this IReadOnlyItem item, IConnection conn, Func<IReadOnlyItem, T> mapper)
    {
      var select = new SubSelect();
      var missingProps = false;
      var watched = new ItemWatcher(item, "", (path, exists) => {
        select.EnsurePath(path.Split('/'));
        missingProps = missingProps || !exists;
      });
      var result = mapper.Invoke(watched);
      if (missingProps)
      {
        if (string.IsNullOrEmpty(item.Id())) throw new ArgumentException(string.Format("No id specified for the item '{0}'", item.ToAml()));
        var aml = conn.AmlContext;
        var query = aml.Item(aml.Action("get"), aml.Type(item.Type().Value), aml.Select(select), aml.Id(item.Id()));
        var res = query.Apply(conn);
        if (res.Items().Any())
        {
          result = mapper.Invoke(res.AssertItem());
        }
        // So the top item couldn't be found (e.g. perhaps this is during an onBeforeAdd).  Now, let's try filling
        // in any multi-level selects
        else if (select.First().Any(s => s.Any()))
        {
          var clone = item.Clone();
          foreach (var multiSelect in select.First().Where(s => s.Any() && s.Name != "id" && s.Name != "config_id"))
          {
            if (clone.Property(multiSelect.Name).HasValue() && clone.Property(multiSelect.Name).Type().HasValue())
            {
              query = aml.Item(aml.Action("get")
                , aml.Type(clone.Property(multiSelect.Name).Type().Value)
                , aml.Select(multiSelect)
                , aml.Id(clone.Property(multiSelect.Name).Value));
              res = query.Apply(conn);
              if (res.Items().Any())
              {
                clone.Property(multiSelect.Name).Set(res.AssertItem());
              }
            }
          }
          result = mapper.Invoke(clone);
        }
      }
      return result;
    }

    private class ItemWatcher : ItemWrapper
    {
      private string _path;
      private Action<string, bool> _listener;

      public ItemWatcher(IReadOnlyItem item, string path, Action<string, bool> listener) : base(item)
      {
        _path = path ?? "";
        _listener = listener;
      }

      public override IReadOnlyProperty Property(string name)
      {
        return new PropertyWatcher(base.Property(name), _path + "/" + name, _listener);
      }

      public override IReadOnlyProperty Property(string name, string lang)
      {
        return new PropertyWatcher(base.Property(name, lang), _path + "/" + name, _listener);
      }
    }

    private class PropertyWatcher : IReadOnlyProperty
    {
      private IReadOnlyProperty _prop;
      private string _path;
      private Action<string, bool> _listener;

      public PropertyWatcher(IReadOnlyProperty prop, string path, Action<string, bool> listener)
      {
        _prop = prop;
        _path = path ?? "";
        _listener = listener;
        _listener(path, _prop.Exists);
      }

      public bool? AsBoolean()
      {
        return _prop.AsBoolean();
      }

      public bool AsBoolean(bool defaultValue)
      {
        return _prop.AsBoolean(defaultValue);
      }

      public DateTime? AsDateTime()
      {
        return _prop.AsDateTime();
      }

      public DateTime AsDateTime(DateTime defaultValue)
      {
        return _prop.AsDateTime(defaultValue);
      }

      public DateTime? AsDateTimeUtc()
      {
        return _prop.AsDateTimeUtc();
      }

      public DateTime AsDateTimeUtc(DateTime defaultValue)
      {
        return _prop.AsDateTimeUtc(defaultValue);
      }

      public double? AsDouble()
      {
        return _prop.AsDouble();
      }

      public double AsDouble(double defaultValue)
      {
        return _prop.AsDouble(defaultValue);
      }

      public Guid? AsGuid()
      {
        return _prop.AsGuid();
      }

      public Guid AsGuid(Guid defaultValue)
      {
        return _prop.AsGuid(defaultValue);
      }

      public int? AsInt()
      {
        return _prop.AsInt();
      }

      public int AsInt(int defaultValue)
      {
        return _prop.AsInt(defaultValue);
      }

      public IReadOnlyItem AsItem()
      {
        return new ItemWatcher(_prop.AsItem(), _path, _listener);
      }

      public string AsString(string defaultValue)
      {
        return _prop.AsString(defaultValue);
      }

      public IReadOnlyAttribute Attribute(string name)
      {
        return _prop.Attribute(name);
      }

      public IEnumerable<IReadOnlyAttribute> Attributes()
      {
        return _prop.Attributes();
      }

      public IEnumerable<IReadOnlyElement> Elements()
      {
        return _prop.Elements();
      }

      public IServerContext Context
      {
        get { return _prop.Context; }
      }

      public bool Exists
      {
        get { return _prop.Exists; }
      }

      public string Name
      {
        get { return _prop.Name; }
      }

      public IReadOnlyElement Parent
      {
        get { return _prop.Parent; }
      }

      public string Value
      {
        get { return _prop.Value; }
      }

      public string ToAml()
      {
        return _prop.ToAml();
      }

      public void ToAml(XmlWriter writer)
      {
        _prop.ToAml(writer);
      }

      public object Clone()
      {
        return _prop.Clone();
      }


      public long? AsLong()
      {
        return _prop.AsLong();
      }

      public long AsLong(long defaultValue)
      {
        return _prop.AsLong(defaultValue);
      }
    }

    private static SubSelect MissingProperties(IReadOnlyItem item, string propName, IEnumerable<SubSelect> properties)
    {
      var result = new SubSelect(propName);
      IReadOnlyProperty itemProp;
      foreach (var prop in properties)
      {
        itemProp = item.Property(prop.Name);
        if (itemProp.Exists)
        {
          if (prop.Any())
          {
            var child = itemProp.AsItem();
            if (child == null)
            {
              result.Add(prop);
            }
            else
            {
              var missing = MissingProperties(child, prop.Name, prop);
              if (missing.Any()) result.Add(missing);
            }
          }
        }
        else
        {
          result.Add(prop);
        }
      }
      return result;
    }
  }
}
