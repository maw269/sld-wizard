<?xml version="1.0" encoding="utf-8"?>
<sld:StyledLayerDescriptor xmlns:sld="http://www.opengis.net/sld"
xmlns:ogc="http://www.opengis.net/ogc"
xmlns:gml="http://www.opengis.net/gml" version="1.0.0">
  <sld:UserLayer>
    <sld:LayerFeatureConstraints>
      <sld:FeatureTypeConstraint />
    </sld:LayerFeatureConstraints>
    <sld:UserStyle>
      <sld:Name>FILE_PREFIX_hyd_soundp</sld:Name>
      <sld:Title>Default Styler</sld:Title>
      <sld:Abstract />
      <sld:FeatureTypeStyle>
        <sld:Name>simple</sld:Name>
        <sld:Title>title</sld:Title>
        <sld:Abstract>abstract</sld:Abstract>
        <sld:FeatureTypeName>Feature</sld:FeatureTypeName>
        <sld:SemanticTypeIdentifier>generic:geometry</sld:SemanticTypeIdentifier>
        <sld:SemanticTypeIdentifier>simple</sld:SemanticTypeIdentifier>
        <sld:Rule>
          <sld:Name>name</sld:Name>
          <sld:Title>title</sld:Title>
          <sld:Abstract>Abstract</sld:Abstract>
          <sld:TextSymbolizer>
            <sld:Label>
              <ogc:PropertyName>hdp</ogc:PropertyName>
            </sld:Label>
            <sld:Font>
              <sld:CssParameter name="font-family">
                <ogc:Literal>Sans Serif</ogc:Literal>
              </sld:CssParameter>
              <sld:CssParameter name="font-size">
                <ogc:Literal>10</ogc:Literal>
              </sld:CssParameter>
              <sld:CssParameter name="font-style">
                <ogc:Literal>normal</ogc:Literal>
              </sld:CssParameter>
            </sld:Font>
            <sld:LabelPlacement>
              <sld:PointPlacement>
                <sld:AnchorPoint>
                  <sld:AnchorPointX>
                    <ogc:Literal>0.0</ogc:Literal>
                  </sld:AnchorPointX>
                  <sld:AnchorPointY>
                    <ogc:Literal>0.0</ogc:Literal>
                  </sld:AnchorPointY>
                </sld:AnchorPoint>
                <sld:Displacement>
                  <sld:DisplacementX>
                    <ogc:Literal>0</ogc:Literal>
                  </sld:DisplacementX>
                  <sld:DisplacementY>
                    <ogc:Literal>0</ogc:Literal>
                  </sld:DisplacementY>
                </sld:Displacement>
                <sld:Rotation>
                  <ogc:Literal>0.0</ogc:Literal>
                </sld:Rotation>
              </sld:PointPlacement>
            </sld:LabelPlacement>
            <sld:Fill>
              <sld:CssParameter name="fill">
                <ogc:Literal>#000000</ogc:Literal>
              </sld:CssParameter>
              <sld:CssParameter name="fill-opacity">
                <ogc:Literal>1.0</ogc:Literal>
              </sld:CssParameter>
            </sld:Fill>
            <sld:VendorOption name="spaceAround">2</sld:VendorOption>
          </sld:TextSymbolizer>
        </sld:Rule>
      </sld:FeatureTypeStyle>
    </sld:UserStyle>
  </sld:UserLayer>
</sld:StyledLayerDescriptor>
