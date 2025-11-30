<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns="">
  <xsl:output omit-xml-declaration="no" indent="yes" />
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='OneHandedAxe']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_11_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_13</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_04_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle04_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle08_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle09_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_13_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_head_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_handle_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_08_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">axe_craft_20_handle_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_axe_head_1</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedAxe']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_08_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle12_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_pommel_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_07_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_axe_head_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_axe_handle_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_axe_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_08_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">axe_craft_20_handle_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle02</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='OneHandedSword']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_13</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_14</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_grip_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_02_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_grip_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_grip_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_pommel_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_sword_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_grip_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_head_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">sickle_handle_1</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='ThrowingAxe']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_13</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_04_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle04_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle08_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle09_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_head_13_blunt</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='OneHandedPolearm']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_13</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">spear_handle_rough_29</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">spear_handle_rough_30</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedPolearm_Pike']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='Javelin']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2_blunt</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedPolearm_Bracing']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_7</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedPolearm_Thrown']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2_blunt</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedPolearm_Couchable']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_7</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedPolearm']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_5</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_8</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_6</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_pommel_7</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_13</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_13_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_1_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">spear_handle_rough_29</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">spear_handle_rough_30</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='TwoHandedSword']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_02_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_13</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_blade_14</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_guard_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_01</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_02</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_03</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_04</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_05</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_06</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_07</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_08</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_09</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_sword_pommel_10</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='Mace']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_head_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_handle_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_mace_pommel_2</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='Dagger']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_blade_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_blade_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_guard_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_guard_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_grip_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_grip_2</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_pommel_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_dagger_pommel_2</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='OneHandedBastardAxe']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_axe_head_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle02</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='OneHandedBastardAxeAlternative']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">coh_axe_head_1</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">naval_axe_handle02</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
  <xsl:template match="/WeaponDescriptions[1]/WeaponDescription[@id='OneHandedPolearm_JavelinAlternative']/AvailablePieces[1]">
    <xsl:copy>
      <xsl:copy-of select="@*" />
      <xsl:apply-templates select="node()" />
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_11</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_12</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2_blunt</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_10</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_9</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_4</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_3</xsl:attribute>
      </xsl:element>
      <xsl:element name="AvailablePiece">
        <xsl:attribute name="id">nord_spear_blade_2</xsl:attribute>
      </xsl:element>
    </xsl:copy>
  </xsl:template>
</xsl:stylesheet>